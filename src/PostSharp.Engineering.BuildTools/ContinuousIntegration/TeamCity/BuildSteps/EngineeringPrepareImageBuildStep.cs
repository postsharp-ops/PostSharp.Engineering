// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Docker;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;

internal class EngineeringPrepareImageBuildStep : PowerShellBuildStep
{
    public DockerSpec DockerSpec { get; }

    public EngineeringPrepareImageBuildStep(
        string id,
        DockerSpec dockerSpec ) : base(
        id,
        $"Prepare Docker image {dockerSpec.ImageName}",
        $"DockerBuild.ps1",
        $"-BuildImage -ImageName {dockerSpec.ImageName}",
        null )
    {
        this.DockerSpec = dockerSpec;
    }
}