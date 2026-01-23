using System;

namespace PostSharp.Engineering.BuildTools.Docker;

internal static class ContainerHelper
{
    public static string GetBuildAgentType( ContainerHostKind hostKind )
        => hostKind switch
        {
            ContainerHostKind.Windows => "docker-win-x64-md",
            ContainerHostKind.Wsl => "docker-wsl-x64-md",
            _ => throw new ArgumentOutOfRangeException( nameof(hostKind) )
        };
}