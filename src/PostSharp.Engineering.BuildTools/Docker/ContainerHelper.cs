// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using System;

namespace PostSharp.Engineering.BuildTools.Docker;

internal static class ContainerHelper
{
    public static BuildAgentRequirement[] GetBuildAgentRequirements( ContainerHostKind hostKind )
        => hostKind switch
        {
            ContainerHostKind.Windows =>
            [
                new BuildAgentRequirement( "teamcity.agent.jvm.os.family", "Windows", RequirementComparisonType.Matches ),
                new BuildAgentRequirement( "teamcity.agent.jvm.os.arch", "amd64", RequirementComparisonType.Matches ),
                new BuildAgentRequirement( "env.BuildAgentType", "docker-win-x64-md" )
            ],

#pragma warning disable CS0612 // Type or member is obsolete
            ContainerHostKind.Wsl =>
            [
                new BuildAgentRequirement( "teamcity.agent.jvm.os.family", "Linux", RequirementComparisonType.Matches ),
                new BuildAgentRequirement( "teamcity.agent.jvm.os.arch", "amd64", RequirementComparisonType.Matches ),
                new BuildAgentRequirement( "env.BuildAgentType", "docker-wsl-x64-md" )
            ],
#pragma warning restore CS0612 // Type or member is obsolete

            ContainerHostKind.Linux =>
            [
                new BuildAgentRequirement( "teamcity.agent.jvm.os.family", "Linux", RequirementComparisonType.Matches ),
                new BuildAgentRequirement( "env.BuildAgentType", "docker-linux-x64-md" )
            ],

            _ => throw new ArgumentOutOfRangeException( nameof(hostKind) )
        };
}