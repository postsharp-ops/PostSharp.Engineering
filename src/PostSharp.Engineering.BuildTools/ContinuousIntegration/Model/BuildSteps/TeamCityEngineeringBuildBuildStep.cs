// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;

public class TeamCityEngineeringBuildBuildStep : TeamCityEngineeringCommandBuildStep
{
    public TeamCityEngineeringBuildBuildStep( BuildConfiguration configuration, bool testOnBuild, DockerSpec? dockerSpec = null ) : base(
        "Build",
        "Build",
        testOnBuild ? "test" : "build",
        $"--configuration {configuration} --buildNumber %build.number% --buildType %system.teamcity.buildType.id%",
        true,
        dockerSpec ) { }
}
