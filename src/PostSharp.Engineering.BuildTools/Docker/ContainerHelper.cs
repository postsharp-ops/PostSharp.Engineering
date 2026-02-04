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
                new BuildAgentRequirement( "env.PROCESSOR_ARCHITECTURE", "AMD64" )
            ],

#pragma warning disable CS0612 // Type or member is obsolete
            ContainerHostKind.Wsl =>
            [
                new BuildAgentRequirement( "teamcity.agent.jvm.os.family", "Linux", RequirementComparisonType.Matches )
            ],
#pragma warning restore CS0612 // Type or member is obsolete

            _ => throw new ArgumentOutOfRangeException( nameof(hostKind) )
        };
}