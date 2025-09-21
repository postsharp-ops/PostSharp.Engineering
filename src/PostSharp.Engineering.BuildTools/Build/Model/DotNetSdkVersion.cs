// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Build.Model;

[PublicAPI]
public sealed record DotNetSdkVersion
{
    public DotNetSdkVersion( string version ) { this.Version = version; }

    public RollForwardPolicy RollForward { get; init; } = RollForwardPolicy.Patch;

    public bool AllowPrerelease { get; init; }

    public string Version { get; }
}