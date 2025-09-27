// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Docker;
using System;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;

internal class EngineeringPublishBuildStep : EngineeringCommandBuildStep
{
    public EngineeringPublishBuildStep( BuildConfiguration configuration, DockerSpec? dockerSpec, TimeSpan? timeSpan ) : base(
        "Publish",
        "Publish",
        "publish",
        $"--configuration {configuration}",
        true,
        dockerSpec,
        timeSpan ) { }
}