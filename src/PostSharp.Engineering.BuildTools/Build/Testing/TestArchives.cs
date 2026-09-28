// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace PostSharp.Engineering.BuildTools.Build.Testing;

/// <summary>
/// The conventions of the test archives of a product whose solutions set <see cref="Solution.ContainsTestApplications"/>. See
/// <c>doc/testing-platform.md</c>.
/// </summary>
internal static class TestArchives
{
    /// <summary>
    /// The name of the script that <c>generate-scripts</c> writes into the engineering directory, and that runs the archives.
    /// </summary>
    public const string ScriptName = "RunTests.ps1";

    /// <summary>
    /// The directory of the archives, relative to the repository root. <c>TestArchive.targets</c> writes them there unless a
    /// project sets <c>TestArchiveDirectory</c>.
    /// </summary>
    public const string Directory = "artifacts/tests";

    /// <summary>
    /// The name of the file, in the engineering directory, that lists the archives that the build configurations of the
    /// test agents download.
    /// </summary>
    public const string ListFileName = "test-archives.txt";

    /// <summary>
    /// Adds <c>PublishTestArchive=true</c> to the properties of the build of the solutions of a product that publishes test
    /// archives, unless the command line sets it. A build in the IDE does not set it, so it does not spend the time of a
    /// publication on every build. On TeamCity, only the build of the configuration that the test agents download the
    /// archives from writes them (see <see cref="IsSourceConfiguration"/>); a local build always writes them, so that
    /// <c>Build.ps1 test</c> can run them.
    /// </summary>
    public static BuildSettings AddBuildProperties( Product product, BuildSettings settings, bool isTeamCityBuild )
        => product.PublishesTestArchives
           && !settings.Properties.ContainsKey( "PublishTestArchive" )
           && (!isTeamCityBuild || IsSourceConfiguration( product, settings.BuildConfiguration ))
            ? settings.WithAdditionalProperties( ImmutableDictionary<string, string>.Empty.Add( "PublishTestArchive", "true" ) )
            : settings;

    /// <summary>
    /// Determines whether a build configuration of the product is the one that <see cref="Product.TestArchivesSourceDependency"/>
    /// names, which writes and publishes the archives. An additional build configuration that the dependency names does it
    /// with the arguments (<c>-p:PublishTestArchive=true</c>) and the artifact rules that the product gives it.
    /// </summary>
    public static bool IsSourceConfiguration( Product product, BuildConfiguration configuration )
        => product.PublishesTestArchives && product.TestArchivesSourceDependency.Configuration == configuration;

    /// <summary>
    /// Checks that an additional build configuration that <see cref="Product.TestArchivesSourceDependency"/> names writes and
    /// publishes the archives. PostSharp.Engineering cannot give it the property and the rule, because it does not know how
    /// the configuration runs the build, so the product gives them; without them, every cell of the test agents would wait
    /// for a build that publishes nothing to download.
    /// </summary>
    public static bool TryValidateSource( Product product, ConsoleHelper console )
    {
        var source = product.TestArchivesSourceDependency;

        if ( source.Configuration != null )
        {
            return true;
        }

        var additional = product.AdditionalCiBuildConfigurations.FirstOrDefault( c => string.Equals( c.Id, source.ConfigurationId, StringComparison.Ordinal ) );

        if ( additional == null )
        {
            // TryGetSourceConfiguration reports it.
            return true;
        }

        var isValid = true;

        if ( additional.ArtifactRules == null || !additional.ArtifactRules.Any( r => r.Contains( Directory + "/", StringComparison.Ordinal ) ) )
        {
            console.WriteError(
                $"The '{additional.Id}' build configuration publishes the test archives (TestArchivesSourceDependency), but none of its artifact "
                + $"rules publishes '{Directory}'. Add '+:{Directory}/*.zip=>{Directory}'." );

            isValid = false;
        }

        if ( additional is PowershellAdditionalCiBuildConfiguration powershell
             && !powershell.Arguments.Contains( "PublishTestArchive=true", StringComparison.OrdinalIgnoreCase ) )
        {
            console.WriteError(
                $"The '{additional.Id}' build configuration publishes the test archives (TestArchivesSourceDependency), but its arguments do not "
                + "set PublishTestArchive. Add '-p:PublishTestArchive=true'." );

            isValid = false;
        }

        return isValid;
    }

    /// <summary>
    /// Gets the build configuration of the build that publishes the archives, <see cref="Product.TestArchivesSourceDependency"/>: the
    /// configuration it names, or the artifacts configuration of the additional build configuration it names.
    /// </summary>
    public static bool TryGetSourceConfiguration( Product product, out BuildConfiguration configuration )
    {
        var source = product.TestArchivesSourceDependency;

        if ( source.Configuration != null )
        {
            configuration = source.Configuration.Value;

            return true;
        }

        var additional = product.AdditionalCiBuildConfigurations.FirstOrDefault( c => string.Equals( c.Id, source.ConfigurationId, StringComparison.Ordinal ) );
        configuration = additional?.EffectiveArtifactsConfiguration ?? default;

        return additional != null;
    }

    private static string GetListPath( BuildContext context ) => Path.Combine( context.RepoDirectory, context.Product.EngineeringDirectory, ListFileName );

    /// <summary>
    /// Writes the list of the archives that <c>generate-scripts</c> planned the build configurations from.
    /// </summary>
    public static void WriteList( BuildContext context, IEnumerable<TestApplication> applications )
    {
        var content = new StringBuilder();
        content.AppendLine( "# The test archives that the build configurations of the test agents download, one per line. This file is" );
        content.AppendLine( "# generated by 'Build.ps1 generate-scripts' from the test applications of the solutions. 'Build.ps1 build'" );
        content.AppendLine( "# fails when the archives it writes differ, which means that the build configurations need regenerating." );

        foreach ( var name in applications.Select( a => a.ArchiveName ).Order( StringComparer.Ordinal ) )
        {
            content.AppendLine( name );
        }

        TextFileHelper.WriteIfDifferent( GetListPath( context ), content.ToString(), context );
    }

    /// <summary>
    /// Compares the archives that the build wrote with the list that <c>generate-scripts</c> wrote, when there is one.
    /// </summary>
    /// <returns><c>false</c> if they differ. An archive missing from the list would be run by no build configuration, and
    /// a listed archive that the build did not write would fail the download of the build configurations that run it.</returns>
    public static bool Verify( BuildContext context )
    {
        var listPath = GetListPath( context );

        if ( !File.Exists( listPath ) )
        {
            return true;
        }

        var expected = File.ReadAllLines( listPath )
            .Select( l => l.Trim() )
            .Where( l => l.Length > 0 && !l.StartsWith( '#' ) )
            .ToHashSet( StringComparer.OrdinalIgnoreCase );

        var archivesDirectory = Path.Combine( context.RepoDirectory, Directory );

        var actual = System.IO.Directory.Exists( archivesDirectory )
            ? System.IO.Directory.GetFiles( archivesDirectory, "*.zip" ).Select( Path.GetFileNameWithoutExtension ).ToHashSet( StringComparer.OrdinalIgnoreCase )
            : [];

        var unexpected = actual.Except( expected ).Order( StringComparer.Ordinal ).ToList();
        var missing = expected.Except( actual ).Order( StringComparer.Ordinal ).ToList();

        if ( unexpected.Count == 0 && missing.Count == 0 )
        {
            return true;
        }

        if ( unexpected.Count > 0 )
        {
            context.Console.WriteError( $"The build wrote test archives that no build configuration runs: {string.Join( ", ", unexpected )}." );
        }

        if ( missing.Count > 0 )
        {
            context.Console.WriteError( $"The build did not write test archives that build configurations run: {string.Join( ", ", missing )}." );
        }

        context.Console.WriteError( $"Run 'Build.ps1 generate-scripts', which plans the build configurations of the test agents and writes '{listPath}'." );

        return false;
    }
}
