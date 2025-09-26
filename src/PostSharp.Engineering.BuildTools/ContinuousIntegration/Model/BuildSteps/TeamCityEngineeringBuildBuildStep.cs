// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Docker;
using System;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;

internal class TeamCityEngineeringBuildBuildStep : TeamCityEngineeringCommandBuildStep
{
    public TeamCityEngineeringBuildBuildStep( BuildConfiguration configuration, bool testOnBuild, DockerSpec? dockerSpec, TimeSpan? timeout ) : base(
        "Build",
        "Build",
        testOnBuild ? "test" : "build",
        $"--configuration {configuration} --buildNumber %build.number% --buildType %system.teamcity.buildType.id%",
        true,
        dockerSpec,
        timeout ) { }
}