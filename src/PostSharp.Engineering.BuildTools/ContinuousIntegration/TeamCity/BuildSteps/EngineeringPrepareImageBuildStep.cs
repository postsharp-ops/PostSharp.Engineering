// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Docker;
using System;

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

    // Preparing an image from scratch (including the base image) should not take more than 60 minutes in the cloud.
    public override TimeSpan AdditionalTimeout => TimeSpan.FromMinutes( 60 );
}