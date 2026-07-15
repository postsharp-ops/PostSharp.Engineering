// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.Build.Publishing;
using PostSharp.Engineering.BuildTools.Build.Swapping;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using PostSharp.Engineering.BuildTools.Utilities;
using System.IO;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

public class DeploymentTests
{
    // A minimal concrete publisher, so the base-class default deployment name can be exercised without depending on a
    // real publisher's construction requirements.
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

    private sealed class TestSwapper : Swapper
    {
        protected override SuccessCode ExecuteCore(
            BuildContext context,
            SwapSettings settings,
            BuildConfigurationInfo configuration,
            BuildArguments buildArguments )
            => SuccessCode.Success;
    }

    [Fact]
    public void Publisher_DefaultsToDefaultDeployment()
    {
        Assert.Equal( "default", new TestPublisher().EffectiveDeploymentName );
        Assert.Equal( "web", new TestPublisher { DeploymentName = "web" }.EffectiveDeploymentName );
    }

    [Fact]
    public void SshPublisher_DefaultsToSshDeployment()
    {
        Assert.Equal( "ssh", new SshPublisher( "host", "user", "/dir" ).EffectiveDeploymentName );

        // An explicit name overrides the "ssh" default.
        Assert.Equal( "prod", new SshPublisher( "host", "user", "/dir" ) { DeploymentName = "prod" }.EffectiveDeploymentName );
    }

    [Fact]
    public void Swapper_DefaultsToDefaultDeployment()
    {
        Assert.Equal( "default", new TestSwapper().EffectiveDeploymentName );
        Assert.Equal( "staging", new TestSwapper { DeploymentName = "staging" }.EffectiveDeploymentName );
    }

    [Fact]
    public void GetPublishDeploymentNames_ReturnsDistinctNamesAndExcludesInertPublishers()
    {
        var configuration = new BuildConfigurationInfo(
            // The SSH publisher is inert at publish time, so its "ssh" deployment is not a b-publish target.
            PublicPublishers: [new TestPublisher { DeploymentName = "web" }, new SshPublisher( "h", "u", "/d" )],
            PrivatePublishers: [new TestPublisher(), new TestPublisher { DeploymentName = "web" }] );

        var names = Publisher.GetPublishDeploymentNames( configuration );

        Assert.Equal( ["web", "default"], names );
    }

    [Fact]
    public void TryValidate_SingleDeployment_NoSelection_Succeeds()
        => Assert.True( DeploymentSelection.TryValidate( new ConsoleHelper(), ["default"], null, "publish", validateExists: true ) );

    [Fact]
    public void TryValidate_MultipleDeployments_NoSelection_Fails()
        => Assert.False( DeploymentSelection.TryValidate( new ConsoleHelper(), ["default", "ssh"], null, "publish", validateExists: true ) );

    [Fact]
    public void TryValidate_MultipleDeployments_ValidSelection_Succeeds()
        => Assert.True( DeploymentSelection.TryValidate( new ConsoleHelper(), ["default", "ssh"], "ssh", "publish", validateExists: true ) );

    [Fact]
    public void TryValidate_UnknownSelection_FailsWhenExistenceIsValidated()
        => Assert.False( DeploymentSelection.TryValidate( new ConsoleHelper(), ["default", "ssh"], "typo", "publish", validateExists: true ) );

    // Swap tolerates a requested deployment that has no swapper of the same name (there is simply nothing to swap).
    [Fact]
    public void TryValidate_UnknownSelection_SucceedsWhenExistenceIsNotValidated()
        => Assert.True( DeploymentSelection.TryValidate( new ConsoleHelper(), ["default"], "web", "swap", validateExists: false ) );

    [Theory]
    [InlineData( "web", "Web" )]
    [InlineData( "web-staging", "WebStaging" )]
    [InlineData( "web.staging_2", "WebStaging2" )]
    [InlineData( "PROD", "PROD" )]
    public void ToObjectNameSuffix_ProducesKotlinSafePascalCase( string deploymentName, string expected )
        => Assert.Equal( expected, TeamCitySettingsFile.ToObjectNameSuffix( deploymentName ) );

    private static TeamCityBuildConfiguration DeploymentConfiguration( string objectName )
        => new(
            objectName,
            objectName,
            "develop/2023.2",
            "SomeVcsId",
            BuildAgentRequirements.Default )
        {
            IsDeployment = true, BuildSteps = []
        };

    // A "Deploy All" is composite (no build agent requirements) and aggregates the individual deployments through
    // snapshot dependencies, so triggering it deploys them all.
    [Fact]
    public void DeployAll_IsCompositeAndDependsOnEveryDeployment()
    {
        // A null BuildAgentRequirements makes the configuration composite.
        var deployAll = new TeamCityBuildConfiguration( "PublicDeployAll", "Deploy All [Public]", "develop/2023.2", "SomeVcsId" )
        {
            BuildSteps = [],
            SnapshotDependencies =
            [
                new TeamCitySnapshotDependency( "PublicDeployment_Web", false ),
                new TeamCitySnapshotDependency( "PublicDeployment_Api", false )
            ]
        };

        var writer = new StringWriter();
        deployAll.GenerateTeamcityCode( writer );
        var code = writer.ToString();

        Assert.Contains( "type = Type.COMPOSITE", code, System.StringComparison.Ordinal );
        Assert.Contains( "snapshot(PublicDeployment_Web)", code, System.StringComparison.Ordinal );
        Assert.Contains( "snapshot(PublicDeployment_Api)", code, System.StringComparison.Ordinal );
    }

    // The deployments and swaps of a build configuration are placed in a sub-project, which TeamCity renders as a folder.
    [Fact]
    public void DeploymentsSubProject_IsGeneratedAsANestedProject()
    {
        var buildConfiguration = new TeamCityBuildConfiguration( "PublicBuild", "Build [Public]", "develop/2023.2", "SomeVcsId", BuildAgentRequirements.Default )
        {
            BuildSteps = []
        };

        var deployAll = new TeamCityBuildConfiguration( "PublicDeployAll", "Deploy All [Public]", "develop/2023.2", "SomeVcsId" ) { BuildSteps = [] };

        var subProject = new TeamCityProject(
            "PublicDeployments",
            "Deployments [Public]",
            [deployAll, DeploymentConfiguration( "PublicDeployment_Web" ), DeploymentConfiguration( "PublicDeployment_Api" )],
            [] );

        var project = new TeamCityProject( [buildConfiguration], [], [subProject] );

        var writer = new StringWriter();
        project.GenerateTeamcityCode( writer );
        var code = writer.ToString();

        // The root project references the sub-project, which is emitted as its own Project object holding the deployments.
        Assert.Contains( "subProject(PublicDeployments)", code, System.StringComparison.Ordinal );
        Assert.Contains( "object PublicDeployments : Project({", code, System.StringComparison.Ordinal );
        Assert.Contains( "name = \"Deployments [Public]\"", code, System.StringComparison.Ordinal );
        Assert.Contains( "buildType(PublicDeployAll)", code, System.StringComparison.Ordinal );
        Assert.Contains( "buildType(PublicDeployment_Web)", code, System.StringComparison.Ordinal );

        // The individual deployment object definitions are emitted too.
        Assert.Contains( "object PublicDeployment_Api : BuildType({", code, System.StringComparison.Ordinal );
    }
}
