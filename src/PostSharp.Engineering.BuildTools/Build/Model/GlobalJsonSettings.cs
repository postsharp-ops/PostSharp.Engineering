// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Build.Model;

public record GlobalJsonSettings
{
    public RollForwardPolicy RollForward { get; init; } = RollForwardPolicy.Patch;

    public bool AllowPrerelease { get; init; }

    public string Version { get; init; } = "8.0";
}