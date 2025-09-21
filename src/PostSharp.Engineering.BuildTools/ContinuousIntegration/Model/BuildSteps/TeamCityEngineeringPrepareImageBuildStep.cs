// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Docker;
using System;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;

public class TeamCityEngineeringPrepareImageBuildStep : TeamCityPowerShellBuildStep
{
    private static string GetCustomArgumentsParameterName( string objectName ) => $"{objectName}Arguments";

    public TeamCityEngineeringPrepareImageBuildStep(
        string id,
        string name,
        ContainerImageSpec containerImageSpec ) : base(
        id,
        name,
        $"DockerBuild.ps1",
        $"-BuildImage -ImageName {containerImageSpec.ImageName}" )
    {
        this.TimeOut = TimeSpan.FromHours( 2 );
    }
}