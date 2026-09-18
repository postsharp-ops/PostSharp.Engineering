// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using System;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

/// <summary>
/// A build configuration that runs the Docker-based tests of one <see cref="DockerTestPlatform"/> by invoking the
/// generated <c>RunDockerTests.ps1</c>.
/// </summary>
/// <remarks>
/// <para>
/// A Docker-based test is a test that needs a container of its own, so the container is what this configuration
/// starts, not what it runs in. The script therefore executes on the agent rather than inside the product build
/// container, which is why the requirements below are a plain <see cref="BuildAgentRequirements"/> and not a
/// <see cref="Docker.ContainerHostRequirements"/>: the latter makes the generator wrap the step in a container of
/// the product image, and a test would then have to nest one engine inside another to get its own.
/// </para>
/// <para>
/// Running on the agent costs nothing, because these tests need no product tool chain on the host. The .NET SDK,
/// MSBuild and Visual Studio that the build container exists to provide all live inside the test's own image.
/// What the host provides is PowerShell and a container engine, and what the build provides is its artifacts,
/// through <see cref="AdditionalCiBuildConfiguration.BuildSnapshotDependency"/>.
/// </para>
/// </remarks>
[PublicAPI]
public class DockerTestsAdditionalCiBuildConfiguration : PowershellAdditionalCiBuildConfiguration
{
    public DockerTestsAdditionalCiBuildConfiguration(
        string id,
        string name,
        DockerTestPlatform platform,
        string path = DefaultPath )

        // The platform is the only argument. Where the tests are is a fact about the repository, not a choice this
        // configuration makes, so generate-scripts writes it into RunDockerTests.ps1 instead.
        : base( id, name, "RunDockerTests.ps1", $"-Platform {GetPlatformIdentifier( platform )}" )
    {
        this.Platform = platform;
        this.Path = path;
        this.BuildAgentRequirements = GetDefaultBuildAgentRequirements( platform );
        this.ProjectFolder = DefaultProjectFolder;

        // This configuration starts containers while running on the agent rather than inside one, so it has no
        // image-preparation step and would otherwise miss the generated cleanup step. It needs that step more
        // than a containerised configuration does: its containers are what write into the checkout as root.
        this.StartsContainers = true;
    }

    /// <summary>
    /// The sub-project these configurations are grouped into. One per platform is several configurations for what a
    /// reader thinks of as one thing, so they are gathered rather than left among the product's own configurations.
    /// </summary>
    public const string DefaultProjectFolder = "Docker Tests";

    /// <summary>
    /// The identifier of the composite that <see cref="WithCompositeConfiguration"/> adds.
    /// </summary>
    public const string CompositeConfigurationId = "RunAllDockerTests";

    /// <summary>
    /// Returns the given configurations, and, when there is more than one, a composite that starts all of them and
    /// reports their combined result.
    /// </summary>
    /// <remarks>
    /// A single platform needs no composite: it would be a second entry point to the one configuration, and it would
    /// report exactly what that configuration already reports. The rule lives here rather than in each product so
    /// that adding a platform is what creates the entry point, rather than something to remember alongside it.
    /// </remarks>
    public static AdditionalCiBuildConfiguration[] WithCompositeConfiguration(
        params DockerTestsAdditionalCiBuildConfiguration[] configurations )
    {
        if ( configurations.Length <= 1 )
        {
            return [..configurations];
        }

        var composite = new CompositeAdditionalCiBuildConfiguration(
            CompositeConfigurationId,
            "Run All Docker Tests",
            [..configurations.Select( c => c.Id )] ) { ProjectFolder = configurations[0].ProjectFolder };

        return [..configurations, composite];
    }

    /// <summary>
    /// The launcher is generated into the engineering directory rather than the repository root, so the step has to
    /// look for it there. The directory is a property of the product, which a configuration does not know when it is
    /// constructed, so the path is resolved here instead.
    /// </summary>
    internal override string GetScriptPath( ProductProperties productProperties )
        => $"{productProperties.Product.EngineeringDirectory}/{this.Script}";

    public DockerTestPlatform Platform { get; }

    /// <summary>The directory holding the test directories, for a product that does not choose another.</summary>
    public const string DefaultPath = "Tests/Docker";

    /// <summary>
    /// Gets the directory holding the test directories, relative to the repository root. It reaches the launcher
    /// through <c>generate-scripts</c>, not through the arguments of this configuration.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the identifier that <c>RunDockerTests.ps1</c> and a test manifest use for a platform.
    /// </summary>
#pragma warning disable CS0618 // Type or member is obsolete
    public static string GetPlatformIdentifier( DockerTestPlatform platform )
        => platform switch
        {
            DockerTestPlatform.WindowsX64 => "win-x64",
            DockerTestPlatform.WindowsArm64 => "win-arm64",
            DockerTestPlatform.LinuxX64 => "linux-x64",
            DockerTestPlatform.LinuxArm64 => "linux-arm64",
            _ => throw new ArgumentOutOfRangeException( nameof(platform) )
        };
#pragma warning restore CS0618 // Type or member is obsolete

    /// <summary>
    /// Gets the requirements that select an agent whose container engine matches the platform.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are expressed in terms of the operating system and architecture the agents actually report, rather
    /// than through <c>env.BuildAgentType</c>. That property names a provisioning profile, and an agent that has a
    /// container engine but was not provisioned as a build container host does not publish it; requiring it would
    /// leave the build queued indefinitely instead of failing.
    /// </para>
    /// <para>
    /// The two ARM64 cases each need a second requirement. The TeamCity JVM on a Windows-on-ARM agent runs under
    /// x64 emulation and reports <c>os.arch=amd64</c>, so architecture alone neither selects it nor excludes it,
    /// and <c>env.PROCESSOR_IDENTIFIER</c> does both. The Linux-ARM64 case admits macOS, because on this farm the
    /// Linux ARM64 engine is the one hosted on the Apple Silicon machine.
    /// </para>
    /// </remarks>
#pragma warning disable CS0618 // Type or member is obsolete
    public static BuildAgentRequirements GetDefaultBuildAgentRequirements( DockerTestPlatform platform )
        => platform switch
        {
            DockerTestPlatform.WindowsX64 => new BuildAgentRequirements(
                new BuildAgentRequirement( "teamcity.agent.jvm.os.family", "Windows", RequirementComparisonType.Matches ),
                new BuildAgentRequirement( "env.PROCESSOR_IDENTIFIER", "ARMv8", RequirementComparisonType.DoesNotContain ) ),

            DockerTestPlatform.WindowsArm64 => new BuildAgentRequirements(
                new BuildAgentRequirement( "teamcity.agent.jvm.os.family", "Windows", RequirementComparisonType.Matches ),
                new BuildAgentRequirement( "env.PROCESSOR_IDENTIFIER", ".*ARMv8.*", RequirementComparisonType.Matches ) ),

            DockerTestPlatform.LinuxX64 => new BuildAgentRequirements(
                new BuildAgentRequirement( "teamcity.agent.jvm.os.name", "Linux" ),
                new BuildAgentRequirement( "teamcity.agent.jvm.os.arch", "amd64" ) ),

            DockerTestPlatform.LinuxArm64 => new BuildAgentRequirements(
                new BuildAgentRequirement( "teamcity.agent.jvm.os.name", "Linux|Mac OS X", RequirementComparisonType.Matches ),
                new BuildAgentRequirement( "teamcity.agent.jvm.os.arch", "aarch64" ) ),

            _ => throw new ArgumentOutOfRangeException( nameof(platform) )
        };
#pragma warning restore CS0618 // Type or member is obsolete
}
