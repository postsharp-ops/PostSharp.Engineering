// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.MSBuild;
using PostSharp.Engineering.BuildTools.Build.Publishing;
using PostSharp.Engineering.BuildTools.Build.Solutions;
using PostSharp.Engineering.BuildTools.Build.Testing;
using PostSharp.Engineering.BuildTools.ContinuousIntegration;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.Docker;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.IO;
using System.Linq;
using Xunit;
using MetalamaDependencies = PostSharp.Engineering.BuildTools.Dependencies.Definitions.MetalamaDependencies;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// A product can test several of its build configurations with a test matrix each: the build of its development workflow,
/// which the pull requests are gated on, and the build that it ships, which the deployment is gated on.
/// </summary>
public sealed class ConfigurationPipelinesTests
{
    private sealed class TestPublisher : Publisher
    {
        protected override bool Publish(
            BuildContext context,
            PublishSettings settings,
            (string Private, string Public) directories,
            BuildConfigurationInfo configuration,
            BuildArguments buildArguments,
            bool isPublic,
            ref bool hasTarget )
            => true;
    }

    private static TestApplication Application( string name, string targetFramework )
        => new( $"C:\\src\\{name}\\{name}.csproj", name, targetFramework, "", ["win-x64"], [], false, null, [] );

    private static Product CreateProductWithTwoSources( params AdditionalCiBuildConfiguration[] configurations )
        => new( MetalamaDependencies.V2026_1.Metalama )
        {
            Solutions = [new DotNetSolution( "Tests.sln" ) { ContainsTestApplications = true }],
            TestAgents = [new TestAgent( "win-x64", "TestWinX64", "Windows x64", BuildAgentRequirements.Empty )],
            TestArchivesSourceDependencies = [new SnapshotDependency( BuildConfiguration.Release ), new SnapshotDependency( BuildConfiguration.Public )],
            AdditionalCiBuildConfigurations = configurations
        };

    /// <summary>
    /// Each source of the archives gets a set of build configurations of its own, which depends on that source and does not
    /// collide with the other set.
    /// </summary>
    [Fact]
    public void EachSourceOfTheArchivesGetsItsOwnSetOfCells()
    {
        var cells = TestArchiveCells.Create( CreateProductWithTwoSources(), [Application( "Common", "net10.0" )] );

        Assert.Equal(
            ["PublicRunAllTestArchives", "PublicTestWinX64Net100", "ReleaseRunAllTestArchives", "ReleaseTestWinX64Net100"],
            cells.Select( c => c.Id ).Order( StringComparer.Ordinal ).ToArray() );

        foreach ( var configuration in new[] { BuildConfiguration.Release, BuildConfiguration.Public } )
        {
            var cell = cells.Single( c => c.Id == $"{configuration}TestWinX64Net100" );

            Assert.Equal( configuration, cell.BuildSnapshotDependency );
            Assert.Equal( configuration, cell.SnapshotDependencies!.Single().Configuration );
            Assert.Equal( $"{configuration}/{TestArchiveCells.DefaultProjectFolder}", cell.ProjectFolder );

            var composite = (CompositeAdditionalCiBuildConfiguration) cells.Single( c => c.Id == $"{configuration}RunAllTestArchives" );
            Assert.Equal( configuration.ToString(), composite.ProjectFolder );
            Assert.Equal( [cell.Id], composite.GetSnapshotDependencies().Select( d => d.ConfigurationId ).ToArray() );
        }

        Assert.True( SnapshotDependencyGraph.TryValidate( new ConsoleHelper(), CreateProductWithTwoSources( [..cells] ) ) );
    }

    /// <summary>
    /// Both sources build and publish the archives.
    /// </summary>
    [Fact]
    public void EverySourceConfigurationPublishesTheArchives()
    {
        var product = CreateProductWithTwoSources();

        Assert.True( TestArchives.IsSourceConfiguration( product, BuildConfiguration.Release ) );
        Assert.True( TestArchives.IsSourceConfiguration( product, BuildConfiguration.Public ) );
        Assert.False( TestArchives.IsSourceConfiguration( product, BuildConfiguration.Debug ) );
    }

    /// <summary>
    /// A project folder that is a path nests the sub-projects, and the package of the build configurations follows it.
    /// </summary>
    [Fact]
    public void AFolderPathNestsTheSubProjects()
    {
        MSBuildHelper.InitializeLocator();

        using var directory = new TempDirectory();

        var product = new Product( MetalamaDependencies.V2026_1.Metalama )
        {
            GenerateDockerfiles = false,
            GenerateTeamCityBuildTypesInSeparateFiles = true,
            OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows ),
            AdditionalCiBuildConfigurations =
            [
                new PowershellAdditionalCiBuildConfiguration( "ReleaseCell", "A cell", "Test.ps1", "" ) { ProjectFolder = "Release/Unit Tests" },
                new PowershellAdditionalCiBuildConfiguration( "PublicCell", "A cell", "Test.ps1", "" ) { ProjectFolder = "Public/Unit Tests" },
                new CompositeAdditionalCiBuildConfiguration( "ReleaseGate", "Quality gate", ["ReleaseCell"] ) { ProjectFolder = "Release" }
            ]
        };

        Assert.True( GenerateScriptsCommand.Execute( TestBuildContext.Create( directory.Path, product ), new CommonCommandSettings() ) );

        var settings = File.ReadAllText( Path.Combine( directory.Path, ".teamcity", "settings.kts" ) );

        Assert.Contains( "subProject(Release)", settings, StringComparison.Ordinal );
        Assert.Contains( "object Release : Project({", settings, StringComparison.Ordinal );
        Assert.Contains( "subProject(ReleaseUnitTests)", settings, StringComparison.Ordinal );
        Assert.Contains( "object ReleaseUnitTests : Project({", settings, StringComparison.Ordinal );
        Assert.Contains( "object PublicUnitTests : Project({", settings, StringComparison.Ordinal );
        Assert.Contains( "name = \"Unit Tests\"", settings, StringComparison.Ordinal );
        Assert.Contains( "import buildTypes.Release.UnitTests.*", settings, StringComparison.Ordinal );

        Assert.True( File.Exists( Path.Combine( directory.Path, ".teamcity", "buildTypes", "Release", "ReleaseGate.kt" ) ) );
        Assert.True( File.Exists( Path.Combine( directory.Path, ".teamcity", "buildTypes", "Release", "UnitTests", "ReleaseCell.kt" ) ) );
        Assert.True( File.Exists( Path.Combine( directory.Path, ".teamcity", "buildTypes", "Public", "UnitTests", "PublicCell.kt" ) ) );
    }

    private static Product CreateProductWithGatedDeployment( string gateId )
        => new( MetalamaDependencies.V2026_1.Metalama )
        {
            GenerateDockerfiles = false,
            OverriddenBuildAgentRequirements = new ContainerRequirements( ContainerHostKind.Windows ),
            AdditionalCiBuildConfigurations =
            [
                new PowershellAdditionalCiBuildConfiguration( "PublicCell", "A cell", "Test.ps1", "" )
                {
                    SnapshotDependencies = [new SnapshotDependency( BuildConfiguration.Public )], BuildSnapshotDependency = BuildConfiguration.Public
                },
                new CompositeAdditionalCiBuildConfiguration( "PublicGate", "Quality gate", ["PublicCell"] )
            ],
            Configurations = Product.DefaultConfigurations.WithValue(
                BuildConfiguration.Public,
                c => c with { PublicPublishers = [new TestPublisher()], DeploymentDependencies = [new SnapshotDependency( gateId )] } )
        };

    /// <summary>
    /// The deployment waits for the quality gate of the build it deploys, and downloads nothing from it.
    /// </summary>
    [Fact]
    public void TheDeploymentWaitsForItsQualityGate()
    {
        MSBuildHelper.InitializeLocator();

        using var directory = new TempDirectory();

        Assert.True(
            GenerateScriptsCommand.Execute( TestBuildContext.Create( directory.Path, CreateProductWithGatedDeployment( "PublicGate" ) ), new CommonCommandSettings() ) );

        var settings = File.ReadAllText( Path.Combine( directory.Path, ".teamcity", "settings.kts" ) );

        var deployment = settings[settings.IndexOf( "object PublicDeployment : BuildType({", StringComparison.Ordinal )..];
        deployment = deployment[..deployment.IndexOf( "\n})", StringComparison.Ordinal )];

        Assert.Contains( "snapshot(PublicGate)", deployment, StringComparison.Ordinal );
        Assert.DoesNotContain( "artifacts(PublicGate)", deployment, StringComparison.Ordinal );
    }

    [Fact]
    public void ADeploymentDependencyMustExist()
    {
        Assert.False( SnapshotDependencyGraph.TryValidate( new ConsoleHelper(), CreateProductWithGatedDeployment( "NoSuchGate" ) ) );
        Assert.True( SnapshotDependencyGraph.TryValidate( new ConsoleHelper(), CreateProductWithGatedDeployment( "PublicGate" ) ) );
    }
}
