// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Testing;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

/// <summary>
/// Creates the build configurations that run the test archives of a product, from its <see cref="Product.TestAgents"/> and
/// the test applications that <see cref="TestApplicationDiscovery"/> found.
/// </summary>
/// <remarks>
/// <para>
/// Each agent gets one build configuration per runtime of the applications that apply to its platform: the target
/// framework without its operating system, so that <c>net10.0</c> and <c>net10.0-windows</c> run together. The
/// applications of each tag of <see cref="TestAgent.SeparateTags"/> run in a build configuration of their own. A skipped
/// application is not downloaded.
/// </para>
/// <para>
/// A build configuration downloads exactly the archives it runs, each by its name, and nothing else of the build. The
/// names are those of the applications that <c>generate-scripts</c> found, so an application added without running it
/// again would be in no build configuration: <see cref="TestArchives.Verify"/> fails the build in that case.
/// </para>
/// <para>
/// A product with several sources of the archives (<see cref="Product.TestArchivesSourceDependencies"/>) gets one set of
/// build configurations per source. The identifiers of a set start with the name of its source, and its project folders are
/// nested in a folder of that name.
/// </para>
/// </remarks>
internal static class TestArchiveCells
{
    public const string CompositeId = "RunAllTestArchives";

    public const string DefaultProjectFolder = "Unit Tests";

    public static ImmutableArray<AdditionalCiBuildConfiguration> Create( Product product, IReadOnlyList<TestApplication> applications )
    {
        var sources = product.EffectiveTestArchivesSourceDependencies;

        if ( sources.Length == 1 )
        {
            return Create( product, applications, sources[0], null );
        }

        return [..sources.SelectMany( s => Create( product, applications, s, ToIdentifier( s.ConfigurationId ?? s.Configuration!.Value.ToString() ) ) )];
    }

    /// <param name="setName">The name of the set of build configurations, which prefixes their identifiers and names the folder
    /// that nests their project folders, or <c>null</c> when the product has a single source of the archives.</param>
    private static ImmutableArray<AdditionalCiBuildConfiguration> Create(
        Product product,
        IReadOnlyList<TestApplication> applications,
        SnapshotDependency source,
        string? setName )
    {
        var cells = new List<AdditionalCiBuildConfiguration>();

        foreach ( var agent in product.TestAgents )
        {
            var runtimes = applications
                .Where( a => a.Skip == null && a.Platforms.Contains( agent.Platform, StringComparer.OrdinalIgnoreCase ) )
                .GroupBy( a => GetRuntime( a.TargetFramework ), StringComparer.OrdinalIgnoreCase )
                .OrderBy( g => g.Key, StringComparer.Ordinal );

            foreach ( var runtime in runtimes )
            {
                var remaining = runtime.ToList();
                var separatedTags = new List<string>();

                // An application with several separate tags runs in the build configuration of the first one.
                foreach ( var tag in agent.SeparateTags )
                {
                    var tagged = remaining.Where( a => a.Tags.Contains( tag, StringComparer.OrdinalIgnoreCase ) ).ToList();

                    if ( tagged.Count > 0 )
                    {
                        cells.Add( CreateCell( product, source, setName, agent, runtime.Key, tag, tagged, separatedTags ) );
                        remaining.RemoveAll( tagged.Contains );
                    }

                    separatedTags.Add( tag );
                }

                if ( remaining.Count > 0 )
                {
                    cells.Add( CreateCell( product, source, setName, agent, runtime.Key, null, remaining, separatedTags ) );
                }
            }
        }

        if ( cells.Count > 0 )
        {
            cells.Add(
                new CompositeAdditionalCiBuildConfiguration(
                    setName + CompositeId,
                    setName == null ? "Run All Test Archives" : $"Run All Test Archives [{setName}]",
                    cells.Select( c => c.Id ).ToArray() ) { ProjectFolder = setName } );
        }

        return [..cells];
    }

    // A part of a TeamCity identifier, which a Kotlin object carries too: letters and digits, the first one in upper case.
    // Two tags that differ only by punctuation give the same identifier, which the validation of the dependency graph
    // reports as two configurations of one identifier.
    private static string ToIdentifier( string value )
    {
        var identifier = string.Concat( value.Where( char.IsAsciiLetterOrDigit ) );

        if ( identifier.Length == 0 )
        {
            throw new InvalidOperationException( $"'{value}' has no letter or digit, and cannot name a build configuration." );
        }

        return char.ToUpperInvariant( identifier[0] ) + identifier[1..];
    }

    // The target framework without its operating system: net10.0-windows runs where net10.0 does.
    private static string GetRuntime( string targetFramework )
    {
        var dash = targetFramework.IndexOf( '-', StringComparison.Ordinal );

        return dash < 0 ? targetFramework : targetFramework[..dash];
    }

    private static TestArchivesCiBuildConfiguration CreateCell(
        Product product,
        SnapshotDependency source,
        string? setName,
        TestAgent agent,
        string runtime,
        string? tag,
        IReadOnlyList<TestApplication> applications,
        IReadOnlyList<string> excludedTags )
    {
        var runtimeId = ToIdentifier( runtime );

        var arguments = $"-Platform {agent.Platform}";

        if ( tag != null )
        {
            arguments += $" -Tags {tag}";
        }

        if ( excludedTags.Count > 0 )
        {
            // RunTests.ps1 splits a comma-separated value, because a build step passes its arguments as text.
            arguments += $" -ExcludeTags {string.Join( ",", excludedTags )}";
        }

        // The archives, and the artifacts that their prepare scripts read, each downloaded to its own path.
        var archiveRules = applications
            .Select( a => $"+:{TestArchives.Directory}/{a.ArchiveName}.zip=>{TestArchives.Directory}" )
            .Concat( applications.SelectMany( a => a.Artifacts ).Select( x => $"+:{x}=>{TestApplication.GetArtifactDirectory( x )}" ) )
            .Distinct( StringComparer.Ordinal )
            .Order( StringComparer.Ordinal )
            .ToArray();

        var resultsDirectory = product.TestResultsDirectory.Replace( '\\', '/' );

        var projectFolder = agent.ProjectFolder ?? DefaultProjectFolder;

        return new TestArchivesCiBuildConfiguration(
            string.Create( CultureInfo.InvariantCulture, $"{setName}{agent.IdPrefix}{runtimeId}{(tag == null ? "" : ToIdentifier( tag ))}" ),
            tag == null ? $"{agent.Name}: {runtime}" : $"{agent.Name}: {runtime} ({tag})",
            arguments )
        {
            BuildAgentRequirements = agent.Requirements,
            Dockerfile = agent.Dockerfile,
            ContainerMemoryInGigabytes = agent.ContainerMemoryInGigabytes,
            ProjectFolder = setName == null ? projectFolder : $"{setName}/{projectFolder}",
            TimeoutInMinutes = agent.TimeoutInMinutes,
            Parameters = agent.Parameters,
            SnapshotDependencies = [source with { ArtifactRules = archiveRules, CleanDestination = true }],

            // The layout of the product build configuration that publishes the archives, when it is one. The cell reads no
            // other artifact of it, but the layout must name the configuration that the cell depends on.
            BuildSnapshotDependency = source.Configuration,
            ArtifactRules = [$"+:{resultsDirectory}/**/*=>{resultsDirectory}"]
        };
    }
}
