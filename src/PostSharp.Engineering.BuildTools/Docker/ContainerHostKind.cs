// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.BuildTools.Docker;

public enum ContainerHostKind
{
    Windows,

    [Obsolete]
    Wsl,

    /// <summary>
    /// A Linux container engine. Used by an image chain whose
    /// <see cref="ContainerRequirements.OperatingSystem"/> is <see cref="ContainerOperatingSystem.Linux"/>.
    /// </summary>
    Linux
}