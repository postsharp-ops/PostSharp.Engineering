// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Arguments;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

/// <summary>
/// A kind of build agent that runs test archives. <c>generate-scripts</c> creates, for each agent of
/// <see cref="Build.Model.Product.TestAgents"/>, one build configuration per runtime of the test applications that apply to
/// its platform. See <c>doc/testing-platform.md</c>.
/// </summary>
/// <param name="Platform">The platform of the agent, as <c>RunTests.ps1 -Platform</c> and <c>TestApplicationPlatforms</c>
/// name it: <c>win-x64</c>, <c>win-arm64</c>, <c>linux-x64</c>, <c>linux-arm64</c>, <c>osx-x64</c> or <c>osx-arm64</c>.</param>
/// <param name="IdPrefix">The beginning of the identifier of each build configuration, for example <c>TestWinX64</c>.</param>
/// <param name="Name">The beginning of the name of each build configuration, for example <c>Windows x64</c>.</param>
/// <param name="Requirements">The requirements of the agent. A <see cref="Docker.ContainerHostRequirements"/> runs the tests in
/// a container of the image that <see cref="Dockerfile"/> names.</param>
[PublicAPI]
public sealed record TestAgent( string Platform, string IdPrefix, string Name, BuildAgentRequirements Requirements )
{
    /// <summary>
    /// Gets the Dockerfile of the image that runs the tests, when <see cref="Requirements"/> is a container host.
    /// </summary>
    public string? Dockerfile { get; init; }

    /// <summary>
    /// Gets the memory of the container, in gigabytes.
    /// </summary>
    public int? ContainerMemoryInGigabytes { get; init; }

    /// <summary>
    /// Gets the TeamCity sub-project that holds the build configurations. Defaults to <c>Unit Tests</c>.
    /// </summary>
    public string? ProjectFolder { get; init; }

    /// <summary>
    /// Gets the timeout of the build configurations.
    /// </summary>
    public int? TimeoutInMinutes { get; init; }

    /// <summary>
    /// Gets the parameters of the build configurations.
    /// </summary>
    public BuildConfigurationParameter[]? Parameters { get; init; }

    /// <summary>
    /// Gets the tags whose test applications run in build configurations of their own, for example the applications whose
    /// tests measure time, so that they run on an agent that nothing else loads.
    /// </summary>
    public string[] SeparateTags { get; init; } = [];
}
