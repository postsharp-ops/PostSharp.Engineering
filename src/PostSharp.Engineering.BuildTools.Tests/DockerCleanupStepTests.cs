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
/// where that is undone, by running the command the agent names in <c>BUILDAGENT_CLEANUP_SCRIPT</c>.
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
        Assert.Contains( "BUILDAGENT_CLEANUP_SCRIPT", kotlin, StringComparison.Ordinal );
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
    /// The containers are removed first. Reclaiming ownership while a container is still writing would leave
    /// whatever it wrote afterwards owned by root again, which is the state the step exists to prevent.
    /// </summary>
    [Fact]
    public void ContainersAreRemovedBeforeTheAgentScriptRuns()
    {
        var kotlin = GenerateKotlin( DockerTestPlatform.LinuxX64 );

        var removal = kotlin.IndexOf( "docker rm", StringComparison.Ordinal );
        var script = kotlin.IndexOf( "BUILDAGENT_CLEANUP_SCRIPT", StringComparison.Ordinal );

        Assert.True( removal >= 0, "The cleanup step no longer removes the containers of the build." );
        Assert.True( script >= 0, "The cleanup step no longer runs the agent's cleanup script." );
        Assert.True( removal < script, "The agent's cleanup script must run after the containers are removed, not before." );
    }

    /// <summary>
    /// An agent that names no command gets none. That is every Windows agent, and every developer machine,
    /// where the ownership question does not arise.
    /// </summary>
    [Fact]
    public void TheAgentScriptIsRunOnlyWhenTheAgentNamesOne()
    {
        var kotlin = GenerateKotlin( DockerTestPlatform.WindowsX64 );

        // The command is generated for every platform; what makes it a no-op is the condition around it,
        // rather than the platform the configuration targets.
        Assert.Contains( "if (${'$'}env:BUILDAGENT_CLEANUP_SCRIPT)", kotlin, StringComparison.Ordinal );
    }
}
