// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.Arguments;
using PostSharp.Engineering.BuildTools.Docker;

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
        ContainerImageSpec? dockerSpec = null ) : base(
        id,
        name,
        dockerSpec != null ? $"DockerBuild.ps1" : "Build.ps1",
        GetScriptArguments( id, command, arguments, areCustomArgumentsAllowed, dockerSpec ) )
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

    private static string GetScriptArguments( string id, string command, string? arguments, bool areCustomArgumentsAllowed, ContainerImageSpec? dockerSpec)
    {
        var args = $"{command}{(arguments == null ? "" : $" {arguments}")}{(!areCustomArgumentsAllowed ? "" : $" %{GetCustomArgumentsParameterName( id )}%")}";

        if ( dockerSpec != null )
        {
            args = $"-ImageName {dockerSpec.ImageName} -NoBuildImage " + args;
        }

        return args;
    }
}