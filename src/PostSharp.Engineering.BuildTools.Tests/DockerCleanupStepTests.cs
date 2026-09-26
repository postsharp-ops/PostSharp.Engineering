// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Build.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using MetalamaDependencies = PostSharp.Engineering.BuildTools.Dependencies.Definitions.MetalamaDependencies;

namespace PostSharp.Engineering.BuildTools.Tests;

/// <summary>
/// A container runs as root while the agent does not, so what a build writes into the mounted repository is
/// owned by root on the host and the agent user cannot remove it afterwards. The generated cleanup step is
/// where that is undone. What it does is in <c>CleanUpBuildAgent.ps1</c> and is tested in
/// <see cref="CleanUpBuildAgentTests"/>; what is tested here is that the step exists, names that script and runs
/// at the right time.
/// </summary>
public sealed class DockerCleanupStepTests
{
    private static string GenerateKotlin( DockerTestPlatform platform )
    {
        var configuration = new DockerTestsAdditionalCiBuildConfiguration(
            "DockerTests",
            "Docker Tests",
            platform ) { BuildSnapshotDependency = BuildConfiguration.Debug };

        var product = new Product( MetalamaDependencies.V2026_1.Metalama ) { AdditionalCiBuildConfigurations = [configuration] };

        var teamCityConfiguration = configuration.TeamCityBuildConfiguration(
            new ProductProperties( product ),
            new Dictionary<BuildConfiguration, TeamCityBuildConfiguration>() );

        // What TeamCitySettingsFile gives every configuration it writes. A configuration without it emits neither
        // clean-up step, which TheStepsAreWiredToTheGeneratedScript asserts does not happen to a generated product.
        teamCityConfiguration.CleanUpBuildAgentScriptPath = $"{product.EngineeringDirectory}/CleanUpBuildAgent.ps1";

        var writer = new StringWriter();
        teamCityConfiguration.GenerateTeamcityCode( writer );

        return writer.ToString();
    }

    /// <summary>
    /// A Docker test configuration runs the launcher on the agent rather than inside a container, so it has no
    /// image-preparation step and the cleanup step used to be omitted from it. It is the configuration that
    /// needs it most: its containers are the ones writing into the checkout.
    /// </summary>
    [Fact]
    public void DockerTestConfigurationGetsTheCleanupStep()
    {
        var kotlin = GenerateKotlin( DockerTestPlatform.LinuxX64 );

        Assert.Contains( "DockerCleanup", kotlin, StringComparison.Ordinal );
        Assert.Contains( "eng/CleanUpBuildAgent.ps1", kotlin, StringComparison.Ordinal );
        Assert.Contains( "-After -BuildLabel %system.teamcity.buildType.id%_%build.number%", kotlin, StringComparison.Ordinal );
    }

    /// <summary>
    /// The step has to run when the build failed, timed out or was stopped, because those are the runs that
    /// leave the checkout in the state that breaks the next build. A step that only ran on success would miss
    /// exactly the cases it exists for.
    /// </summary>
    [Fact]
    public void TheCleanupStepAlwaysRuns()
    {
        var kotlin = GenerateKotlin( DockerTestPlatform.LinuxX64 );
        var cleanup = kotlin[kotlin.IndexOf( "DockerCleanup", StringComparison.Ordinal )..];

        Assert.Contains( "ExecutionMode.ALWAYS", cleanup, StringComparison.Ordinal );
    }

    /// <summary>
    /// The step names the script rather than carrying what it does. A settings file holding that inline was one very
    /// long line per build configuration, where a defect -- a removal whose failure was silently absorbed -- had gone
    /// unnoticed for as long as it had partly because nothing about such a line invites reading.
    /// </summary>
    [Fact]
    public void TheCleanupStepsCarryNoInlineScript()
    {
        var kotlin = GenerateKotlin( DockerTestPlatform.LinuxX64 );

        Assert.DoesNotContain( "BUILDAGENT_CLEANUP_SCRIPT", kotlin, StringComparison.Ordinal );
        Assert.DoesNotContain( "docker rm", kotlin, StringComparison.Ordinal );
        Assert.DoesNotContain( "Remove-Item", kotlin, StringComparison.Ordinal );
    }

    /// <summary>
    /// The clean-up before the build, which every configuration with steps gets: the packages of an earlier build reach
    /// the cache of the agent whether or not this configuration uses a container.
    /// </summary>
    [Fact]
    public void TheNuGetCacheIsCleanedByTheSameScriptBeforeTheBuild()
    {
        var kotlin = GenerateKotlin( DockerTestPlatform.WindowsX64 );

        var clean = kotlin.IndexOf( "CleanNuGetCache", StringComparison.Ordinal );
        var after = kotlin.IndexOf( "-After", StringComparison.Ordinal );

        Assert.True( clean >= 0, "The build no longer cleans the NuGet cache." );
        Assert.True( clean < after, "The clean-up before the build must come before the one after it." );

        // Before the build it takes no argument: the packages it deletes are baked into the script by generate-scripts.
        Assert.Contains( "eng/CleanUpBuildAgent.ps1", kotlin, StringComparison.Ordinal );
    }
}
