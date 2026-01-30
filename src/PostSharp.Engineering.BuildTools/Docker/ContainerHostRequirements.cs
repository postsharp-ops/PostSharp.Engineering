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

    /// <summary>
    /// Gets or sets the memory limit in GB for Docker containers. Default is 8 GB.
    /// </summary>
    public int Memory { get; init; } = 8;

    public ContainerHostRequirements( ContainerHostKind hostKind ) : base(
        new BuildAgentRequirement( "env.BuildAgentType", ContainerHelper.GetBuildAgentType( hostKind ) ) )
    {
        this.HostKind = hostKind;
    }
}