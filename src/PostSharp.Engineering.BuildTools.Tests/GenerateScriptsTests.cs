// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.Dependencies.Definitions;
using PostSharp.Engineering.BuildTools.Dependencies.Model;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

public class GenerateScriptsTests
{
    // Writing 'eng/DockerMounts.g.ps1' requires the dependencies to have been fetched or restored, but the file is
    // excluded from source control and holds machine-local paths. The command must therefore still write the tracked
    // scripts and succeed, as required by the upstream merge, which regenerates the scripts after a conflict
    // resolution in a checkout that has no 'dependencies' directory.
    [Fact]
    public void Execute_SucceedsWhenTheDependenciesCannotBeRead()
    {
        // Reading the dependencies goes through MSBuild, whose assemblies are located at run time. Without this call,
        // the method that reads them cannot even be entered in a test host.
        MSBuildHelper.InitializeLocator();

        using var directory = new TempDirectory();

        var product = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            // The TeamCity settings and the Dockerfiles have their own tests and need a full product definition.
            GenerateTeamCitySettings = false,
            GenerateDockerfiles = false,
            OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows )
        };

        var context = TestBuildContext.Create( directory.Path, product );

        // The directory has no 'eng/Versions.props', so the dependency configuration cannot be read.
        Assert.True( GenerateScriptsCommand.Execute( context, new CommonCommandSettings() ) );

        // The tracked generated files, which are the contract of this command, have been written.
        Assert.True( File.Exists( Path.Combine( directory.Path, "Build.ps1" ) ) );
        Assert.True( File.Exists( Path.Combine( directory.Path, "build.sh" ) ) );
        Assert.True( File.Exists( Path.Combine( directory.Path, "DockerBuild.ps1" ) ) );
        Assert.True( File.Exists( Path.Combine( directory.Path, "eng", "RunClaude.ps1" ) ) );

        // The git-ignored mount file has been skipped.
        Assert.False( File.Exists( Path.Combine( directory.Path, "eng", "DockerMounts.g.ps1" ) ) );
    }

    /// <summary>
    /// A consolidated product drives the build of the other repositories through Orchestrator.ps1, whose product list
    /// used to be maintained by hand in the consolidated repository. A product missing from that list is never bumped
    /// and never deployed, and nothing reports it, so the list is generated from the source dependencies instead.
    /// </summary>
    [Fact]
    public void Orchestrator_ListsEverySourceDependencyThenTheRepositoryItself()
    {
        MSBuildHelper.InitializeLocator();

        using var directory = new TempDirectory();

        var product = new Product( MetalamaDependencies.V2027_0.Consolidated )
        {
            GenerateTeamCitySettings = false,
            GenerateDockerfiles = false,
            OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows )
        };

        Assert.True( GenerateScriptsCommand.Execute( TestBuildContext.Create( directory.Path, product ), new CommonCommandSettings() ) );

        var orchestrator = Path.Combine( directory.Path, "Orchestrator.ps1" );
        Assert.True( File.Exists( orchestrator ) );

        // The line endings of the generated file are those of the embedded resource, which differ from the ones a
        // literal in this file takes when it is checked out, so both sides are normalized before they are compared.
        var text = NormalizeLineEndings( File.ReadAllText( orchestrator ) );

        // Backstage comes first because it is declared first, which is the order the products have to be built in, and
        // the repository of the consolidated product itself comes last.
        var expectedProducts = string.Join(
            "\n",
            "$products = @(",
            "    \"$repo/source-dependencies/Backstage\",",
            "    \"$repo/source-dependencies/Metalama.Compiler\",",
            "    \"$repo/source-dependencies/Metalama\",",
            "    \"$repo/source-dependencies/Metalama.Community\",",
            "    \"$repo/source-dependencies/Metalama.Premium\",",
            "    \"$repo/source-dependencies/Metalama.Samples\",",
            "    \"$repo/source-dependencies/Metalama.Documentation\",",
            "    \"$repo/source-dependencies/Metalama.Tests.NopCommerce\",",
            "    $repo",
            ")" );

        Assert.Contains( expectedProducts, text, StringComparison.Ordinal );

        // The hand-written script ran the last product a second time when every product had succeeded, so the
        // consolidated repository bumped, pre-published and post-published twice.
        Assert.DoesNotContain( "& $fullPath @args\n    exit 0", text, StringComparison.Ordinal );
    }

    /// <summary>
    /// Only a consolidated product drives the build of other repositories, so no other product is given the script.
    /// </summary>
    [Fact]
    public void Orchestrator_IsNotGeneratedForAnOrdinaryProduct()
    {
        MSBuildHelper.InitializeLocator();

        using var directory = new TempDirectory();

        var product = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            GenerateTeamCitySettings = false,
            GenerateDockerfiles = false,
            OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows )
        };

        Assert.True( GenerateScriptsCommand.Execute( TestBuildContext.Create( directory.Path, product ), new CommonCommandSettings() ) );

        Assert.False( File.Exists( Path.Combine( directory.Path, "Orchestrator.ps1" ) ) );
    }

    /// <summary>
    /// The order is a topological sort of the dependency graph. Declaration order only breaks ties between products
    /// that no dependency relates, so that the generated script changes when the graph does and not otherwise.
    /// </summary>
    [Fact]
    public void BuildOrder_FollowsTheDependencyGraph()
    {
        var ordered = EmbeddedResourceHelper.GetBuildOrder( MetalamaDependencies.V2027_0.Consolidated.SourceDependencies )
            .Select( d => d.Name );

        Assert.Equal(
            [
                "Backstage",
                "Metalama.Compiler",
                "Metalama",
                "Metalama.Community",
                "Metalama.Premium",
                "Metalama.Samples",
                "Metalama.Documentation",
                "Metalama.Tests.NopCommerce"
            ],
            ordered );
    }

    /// <summary>
    /// Every product follows the products it depends on, directly or through another product. This is the property the
    /// order exists for: a product reads the versions of its dependencies, so a dependency bumped after its consumer
    /// leaves the consumer pinned to the previous version. It is asserted on the reversed input as well, because an
    /// order that merely echoed its input would satisfy the declared one by accident.
    /// </summary>
    [Fact]
    public void BuildOrder_PutsEveryDependencyBeforeItsConsumer()
    {
        AssertBuildOrder( MetalamaDependencies.V2027_0.Consolidated );
        AssertBuildOrder( PostSharpDependencies.V2027_0.Consolidated );

        static void AssertBuildOrder( DependencyDefinition consolidated )
        {
            AssertOrderOf( consolidated, consolidated.SourceDependencies );
            AssertOrderOf( consolidated, Enumerable.Reverse( consolidated.SourceDependencies ).ToArray() );
        }

        static void AssertOrderOf( DependencyDefinition consolidated, DependencyDefinition[] input )
        {
            var ordered = EmbeddedResourceHelper.GetBuildOrder( input );

            Assert.Equal( input.Length, ordered.Length );

            for ( var i = 0; i < ordered.Length; i++ )
            {
                var reachable = GetReachable( ordered[i] );

                for ( var j = i + 1; j < ordered.Length; j++ )
                {
                    Assert.False(
                        reachable.Contains( ordered[j] ),
                        $"'{consolidated.Name}' builds '{ordered[i].Name}' before '{ordered[j].Name}', which it depends on." );
                }
            }
        }

        static HashSet<DependencyDefinition> GetReachable( DependencyDefinition definition )
        {
            var reachable = new HashSet<DependencyDefinition>();
            var pending = new Stack<DependencyDefinition>();
            pending.Push( definition );

            while ( pending.Count > 0 )
            {
                var current = pending.Pop();

                foreach ( var next in current.Dependencies.Select( d => d.Definition ).Concat( current.SourceDependencies ) )
                {
                    if ( reachable.Add( next ) )
                    {
                        pending.Push( next );
                    }
                }
            }

            return reachable;
        }
    }

    private static string NormalizeLineEndings( string text ) => text.Replace( "\r\n", "\n", StringComparison.Ordinal );

    [Fact]
    public void GetUnfetchedDependencies_ReturnsTheDependenciesThatHaveNoVersionFile()
    {
        var fetched = DependencySource.CreateRestoredDependency( null, DependencyConfigurationOrigin.Default );
        fetched.VersionFile = "dependencies/Fetched/Fetched.version.props";

        var dependencies = new Dictionary<string, DependencySource>
        {
            // A feed dependency is consumed from a package feed, so it has no local directory to mount.
            ["Feed"] = DependencySource.CreateFeed( "1.0.0", DependencyConfigurationOrigin.Default ),
            ["Fetched"] = fetched,
            ["Unfetched"] = DependencySource.CreateRestoredDependency( null, DependencyConfigurationOrigin.Default )
        };

        Assert.Equal( new[] { "Unfetched" }, GenerateScriptsCommand.GetUnfetchedDependencies( dependencies ) );
    }
}
