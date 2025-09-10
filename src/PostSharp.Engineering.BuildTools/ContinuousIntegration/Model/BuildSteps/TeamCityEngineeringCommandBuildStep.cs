// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.Arguments;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;

public class TeamCityEngineeringCommandBuildStep : TeamCityPowerShellBuildStep
{
    private static string GetCustomArgumentsParameterName( string objectName ) => $"{objectName}Arguments";

    public TeamCityEngineeringCommandBuildStep(
        string id,
        string name,
        string command,
        string? arguments = null,
        bool areCustomArgumentsAllowed = false,
        bool useDocker = false ) : base(
        id,
        name,
         useDocker ? "DockerBuild.ps1" : "Build.ps1",
        $"{command}{(arguments == null ? "" : $" {arguments}")}{(!areCustomArgumentsAllowed ? "" : $" %{GetCustomArgumentsParameterName( id )}%")}" )
    {
        if ( areCustomArgumentsAllowed )
        {
            this.BuildConfigurationParameters =
            [
                new TeamCityTextBuildConfigurationParameterBase(
                    GetCustomArgumentsParameterName( id ),
                    $"{name} Arguments",
                    $"Arguments to append to the '{name}' build step.",
                    allowEmpty: true )
            ];
        }
    }
}