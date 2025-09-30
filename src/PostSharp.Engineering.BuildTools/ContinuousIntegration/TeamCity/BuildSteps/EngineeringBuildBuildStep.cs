// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Docker;
using System;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;

internal class EngineeringBuildBuildStep : EngineeringCommandBuildStep
{
    public EngineeringBuildBuildStep(
        BuildConfiguration configuration,
        string checkoutDirectory,
        bool testOnBuild,
        DockerSpec? dockerSpec,
        TimeSpan? timeout ) : base(
        "Build",
        "Build",
        checkoutDirectory,
        testOnBuild ? "test" : "build",
        $"--configuration {configuration} --buildNumber %build.number% --buildType %system.teamcity.buildType.id%",
        true,
        dockerSpec,
        timeout ) { }
}