// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Arguments;
using PostSharp.Engineering.BuildTools.Docker;
using System;
using System.Globalization;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;

internal class EngineeringCommandBuildStep : PowerShellBuildStep
{
    private static string GetTimeoutParameterName( string id ) => $"{id}.Timeout";

    public EngineeringCommandBuildStep(
        string id,
        string name,
        string command,
        string? arguments = null,
        bool areCustomArgumentsAllowed = false,
        DockerSpec? dockerSpec = null,
        TimeSpan? timeout = null ) : base(
        id,
        name,
        "Build.ps1",
        GetScriptArguments( id, command, arguments, timeout ),
        dockerSpec,
        areCustomArgumentsAllowed )
    {
        if ( timeout != null )
        {
            this.AddParameter(
                new BuildConfigurationParameter(
                    GetTimeoutParameterName( id ),
                    timeout.Value.TotalMinutes.ToString( CultureInfo.InvariantCulture ) ) );
        }
    }

    private static string GetScriptArguments(
        string id,
        string command,
        string? arguments,
        TimeSpan? timeout )
    {
        var args = $"{command}{(arguments == null ? "" : $" {arguments}")}";

        if ( timeout != null )
        {
            args += $" --timeout %{GetTimeoutParameterName( id )}%";
        }

        return args;
    }
}