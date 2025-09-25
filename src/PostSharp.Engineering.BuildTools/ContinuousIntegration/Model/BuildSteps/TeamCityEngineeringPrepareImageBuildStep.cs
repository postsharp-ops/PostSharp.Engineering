// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Docker;
using System;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;

internal class TeamCityEngineeringPrepareImageBuildStep : TeamCityPowerShellBuildStep
{
    public DockerSpec DockerSpec { get; }

    public TeamCityEngineeringPrepareImageBuildStep(
        string id,
        DockerSpec dockerSpec ) : base(
        id,
        $"Prepare Docker image {dockerSpec.ImageName}",
        $"DockerBuild.ps1",
        $"-BuildImage -ImageName {dockerSpec.ImageName}" )
    {
        this.DockerSpec = dockerSpec;
    }
}