// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

namespace PostSharp.Engineering.BuildTools.Docker;

/// <summary>
/// Indicates that the build script must execute on the Docker host (presumably, the script itself then initializes the container).
/// </summary>
[PublicAPI]
public record ContainerHostRequirements : BuildAgentRequirements
{
    public ContainerHostKind HostKind { get; }

    public ContainerHostRequirements( ContainerHostKind hostKind ) : base(
        ContainerHelper.GetBuildAgentRequirements( hostKind ) )
    {
        this.HostKind = hostKind;
    }

    public override bool IsDockerized => true;
}