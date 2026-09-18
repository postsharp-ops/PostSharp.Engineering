// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using MetalamaDependencies = PostSharp.Engineering.BuildTools.Dependencies.Definitions.MetalamaDependencies;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// A Docker-based test starts a container of its own, so the configuration that runs it must execute on the agent
/// rather than inside the product build container.
/// </summary>
public sealed class DockerTestsBuildConfigurationTests
{
    private static PowerShellScriptBuildStep GetExecutionStep( DockerTestPlatform platform, string path = "Tests/Docker" )
    {
        var configuration = new DockerTestsAdditionalCiBuildConfiguration(
            "DockerTests",
            "Docker Tests",
            platform,
            path ) { BuildSnapshotDependency = BuildConfiguration.Debug };

        var product = new Product( MetalamaDependencies.V2026_1.Metalama ) { AdditionalCiBuildConfigurations = [configuration] };

        var teamCityConfiguration = configuration.TeamCityBuildConfiguration(
            new ProductProperties( product ),
            new Dictionary<BuildConfiguration, TeamCityBuildConfiguration>() );

        return teamCityConfiguration.BuildSteps!.OfType<PowerShellScriptBuildStep>().Last();
    }

    /// <summary>
    /// The launcher is what the agent runs. A configuration whose requirements are a
    /// <see cref="Docker.ContainerHostRequirements"/> has its step rewritten to <c>DockerBuild.ps1</c> with the real
    /// script passed inside, which is exactly what these configurations must not do: the tests would then have to
    /// nest one container engine inside another to get a container of their own.
    /// </summary>
    [Fact]
    public void ScriptRunsOnTheAgentRatherThanInAContainer()
    {
        var step = GetExecutionStep( DockerTestPlatform.LinuxX64 );

        // A step carrying a DockerSpec has its ScriptPath rewritten to DockerBuild.ps1, with the real script passed
        // to it as an argument. The launcher being the script path is therefore what says it runs on the agent.
        Assert.Equal( "eng/RunDockerTests.ps1", step.ScriptPath );
        Assert.DoesNotContain( "DockerBuild.ps1", step.ScriptArguments, StringComparison.Ordinal );
    }

    /// <summary>
    /// The platform is the only thing a configuration chooses. Where the tests are is a fact about the repository,
    /// carried by the launcher that is generated into its root; where what they consume is, is a fact about the
    /// product, which each test resolves for itself.
    /// </summary>
    [Fact]
    public void ArgumentsCarryThePlatformAndNothingElse()
    {
        var step = GetExecutionStep( DockerTestPlatform.WindowsX64, "Tests/Containers" );

        Assert.Contains( "-Platform win-x64", step.ScriptArguments, StringComparison.Ordinal );
        Assert.DoesNotContain( "-Path", step.ScriptArguments, StringComparison.Ordinal );
        Assert.DoesNotContain( "-InputDirectory", step.ScriptArguments, StringComparison.Ordinal );
    }

    /// <summary>
    /// The step has to name the launcher where generate-scripts puts it. The two are decided in different places --
    /// the configuration builds the step, the generator writes the file -- so nothing but a test holds them together,
    /// and when they disagreed the agent failed with "Cannot find PowerShell script by path specified in build
    /// configuration settings" after the build had already been queued and an agent assigned.
    /// </summary>
    [Fact]
    public void ScriptPathIsWhereTheLauncherIsGenerated()
    {
        var step = GetExecutionStep( DockerTestPlatform.LinuxX64 );
        var product = new Product( MetalamaDependencies.V2026_1.Metalama );

        Assert.Equal( $"{product.EngineeringDirectory}/RunDockerTests.ps1", step.ScriptPath );

        // Not at the root, which is where it used to be.
        Assert.NotEqual( "RunDockerTests.ps1", step.ScriptPath );
    }

    // The obsolete platform is covered on purpose: the identifier it maps to must not drift while it is still
    // declared, because a configuration written against it would otherwise change meaning silently.
#pragma warning disable CS0618 // Type or member is obsolete
    [Theory]
    [InlineData( DockerTestPlatform.WindowsX64, "win-x64" )]
    [InlineData( DockerTestPlatform.WindowsArm64, "win-arm64" )]
    [InlineData( DockerTestPlatform.LinuxX64, "linux-x64" )]
    [InlineData( DockerTestPlatform.LinuxArm64, "linux-arm64" )]
    public void PlatformIdentifierIsTheOneTheScriptAndTheManifestUse( DockerTestPlatform platform, string expected )
        => Assert.Equal( expected, DockerTestsAdditionalCiBuildConfiguration.GetPlatformIdentifier( platform ) );

    private static DockerTestsAdditionalCiBuildConfiguration CreateConfiguration( DockerTestPlatform platform )
        => new( $"DockerTests{platform}", $"Docker Tests ({platform})", platform );

    /// <summary>
    /// A composite is what lets a person start the Docker tests of every platform, and read one result, rather than
    /// starting one configuration per platform and reading each.
    /// </summary>
    [Fact]
    public void SeveralPlatformsGainACompositeThatStartsThemAll()
    {
        var x64 = CreateConfiguration( DockerTestPlatform.LinuxX64 );
        var arm64 = CreateConfiguration( DockerTestPlatform.LinuxArm64 );

        var configurations = DockerTestsAdditionalCiBuildConfiguration.WithCompositeConfiguration( x64, arm64 );

        var composite = Assert.Single( configurations.OfType<CompositeAdditionalCiBuildConfiguration>() );
        Assert.Equal( DockerTestsAdditionalCiBuildConfiguration.CompositeConfigurationId, composite.Id );
        Assert.Equal( [x64.Id, arm64.Id], composite.DependencyIds );

        // The composite belongs in the same sub-project as what it aggregates; outside it, it reads as an unrelated
        // configuration of the product.
        Assert.Equal( x64.ProjectFolder, composite.ProjectFolder );
    }

    /// <summary>
    /// One platform needs no composite: it would be a second entry point reporting exactly what the single
    /// configuration already reports.
    /// </summary>
    [Fact]
    public void OnePlatformGainsNoComposite()
    {
        var configurations = DockerTestsAdditionalCiBuildConfiguration.WithCompositeConfiguration(
            CreateConfiguration( DockerTestPlatform.LinuxX64 ) );

        Assert.Empty( configurations.OfType<CompositeAdditionalCiBuildConfiguration>() );
        Assert.Single( configurations );
    }

    /// <summary>
    /// The configurations are grouped without the product having to say so, because one per platform is several
    /// configurations for what a reader thinks of as one thing.
    /// </summary>
    [Fact]
    public void ConfigurationsAreGroupedIntoTheirOwnSubProject()
        => Assert.Equal(
            DockerTestsAdditionalCiBuildConfiguration.DefaultProjectFolder,
            CreateConfiguration( DockerTestPlatform.LinuxX64 ).ProjectFolder );

    /// <summary>
    /// The requirements name the operating system and the architecture the agents report. Requiring
    /// <c>env.BuildAgentType</c> instead would leave the build queued indefinitely on a farm whose agent has a
    /// container engine but was not provisioned as a build container host.
    /// </summary>
    [Fact]
    public void RequirementsSelectTheEngineByOperatingSystemAndArchitecture()
    {
        var linux = DockerTestsAdditionalCiBuildConfiguration.GetDefaultBuildAgentRequirements( DockerTestPlatform.LinuxX64 );

        Assert.Contains( linux.Items, i => i is { Name: "teamcity.agent.jvm.os.name", Value: "Linux" } );
        Assert.Contains( linux.Items, i => i is { Name: "teamcity.agent.jvm.os.arch", Value: "amd64" } );
        Assert.DoesNotContain( linux.Items, i => i.Name == "env.BuildAgentType" );

        // The requirements must not be dockerized: that is what decides whether the generator wraps the step.
        Assert.False( linux.IsDockerized );
    }

    /// <summary>
    /// The TeamCity JVM on a Windows-on-ARM agent runs under x64 emulation and reports <c>os.arch=amd64</c>, so the
    /// architecture alone neither selects that agent nor excludes it.
    /// </summary>
    [Fact]
    public void WindowsRequirementsDiscriminateArm64ByProcessorIdentifier()
    {
        var x64 = DockerTestsAdditionalCiBuildConfiguration.GetDefaultBuildAgentRequirements( DockerTestPlatform.WindowsX64 );
        var arm64 = DockerTestsAdditionalCiBuildConfiguration.GetDefaultBuildAgentRequirements( DockerTestPlatform.WindowsArm64 );

        Assert.Contains(
            x64.Items,
            i => i is { Name: "env.PROCESSOR_IDENTIFIER", ComparisonType: RequirementComparisonType.DoesNotContain } );

        Assert.Contains(
            arm64.Items,
            i => i is { Name: "env.PROCESSOR_IDENTIFIER", ComparisonType: RequirementComparisonType.Matches } );
    }
#pragma warning restore CS0618 // Type or member is obsolete
}
