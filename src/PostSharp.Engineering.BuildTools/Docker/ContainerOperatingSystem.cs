// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Docker;

public enum ContainerOperatingSystem
{
    Windows2025,
    Default = Windows2025,
    Windows2022,
    Linux
}

public enum OperatingSystemFamily
{
    Windows,
    Linux
}