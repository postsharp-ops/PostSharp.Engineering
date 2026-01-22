using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;

namespace PostSharp.Engineering.BuildTools.Docker;

/// <summary>
/// Indicates that the build script must execute on the Docker host (presumably, the script itself then initializes the container).
/// </summary>
[PublicAPI]
public record ContainerHostRequirements : BuildAgentRequirements
{
    public ContainerHostRequirements( ContainerHostKind hostKind ) : base( new BuildAgentRequirement( "env.BuildAgentType", ContainerHelper.GetBuildAgentType( hostKind ) ) ) { }
}