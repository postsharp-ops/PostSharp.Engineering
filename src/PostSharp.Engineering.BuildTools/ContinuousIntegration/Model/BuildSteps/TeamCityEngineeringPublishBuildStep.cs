// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;

public class TeamCityEngineeringPublishBuildStep : TeamCityEngineeringCommandBuildStep
{
    public TeamCityEngineeringPublishBuildStep( BuildConfiguration configuration, DockerSpec? dockerSpec = null ) : base(
        "Publish",
        "Publish",
        "publish",
        $"--configuration {configuration}",
        true, dockerSpec ) { }
}