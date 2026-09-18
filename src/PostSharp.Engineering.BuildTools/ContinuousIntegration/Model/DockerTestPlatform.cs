// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

/// <summary>
/// The operating system and processor architecture of the container engine that runs a set of Docker-based tests.
/// </summary>
/// <remarks>
/// This is the platform of the engine, not of the agent. They are normally the same, but they need not be: a
/// Windows host in Linux-container mode runs Linux images. <c>RunDockerTests.ps1</c> asks the engine for its own
/// platform and refuses to run when it disagrees with the one it was given, so a build routed to the wrong agent
/// fails once with that reason rather than once per test with an unrelated one.
/// </remarks>
[PublicAPI]
public enum DockerTestPlatform
{
    WindowsX64,

    /// <summary>
    /// Windows containers on ARM64. Not supported: no agent in the farm has a Windows container engine on that
    /// architecture, so a configuration declaring this platform would stay queued rather than fail, which is the
    /// hardest kind of misconfiguration to notice. It is kept so that the identifier keeps its meaning wherever
    /// one was already written.
    /// </summary>
    [Obsolete( "There is no Windows ARM64 container host in the build farm, so this platform cannot run." )]
    WindowsArm64,

    LinuxX64,
    LinuxArm64
}
