// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Docker;

/// <summary>
/// The processor architecture of a container image. It matters only where a component's installation media
/// differs per architecture; most components are architecture-neutral.
/// </summary>
public enum ContainerArchitecture
{
    X64,
    Arm64
}
