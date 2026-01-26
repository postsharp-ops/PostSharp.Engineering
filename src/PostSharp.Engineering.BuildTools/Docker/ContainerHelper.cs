// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;

namespace PostSharp.Engineering.BuildTools.Docker;

internal static class ContainerHelper
{
    public static string GetBuildAgentType( ContainerHostKind hostKind )
        => hostKind switch
        {
            ContainerHostKind.Windows => "docker-win-x64-md",

#pragma warning disable CS0612 // Type or member is obsolete
            ContainerHostKind.Wsl => "docker-wsl-x64-md",
#pragma warning restore CS0612 // Type or member is obsolete

            _ => throw new ArgumentOutOfRangeException( nameof(hostKind) )
        };
}