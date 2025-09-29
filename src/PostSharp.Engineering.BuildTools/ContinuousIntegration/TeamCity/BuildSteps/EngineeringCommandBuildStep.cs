// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Arguments;
using PostSharp.Engineering.BuildTools.Docker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;

internal class EngineeringCommandBuildStep : PowerShellBuildStep
{
    private static string GetCustomArgumentsParameterName( string id ) => $"{id}.Arguments";

    private static string GetTimeoutParameterName( string id ) => $"{id}.Timeout";

    public EngineeringCommandBuildStep(
        string id,
        string name,
        string checkoutDirectory,
        string command,
        string? arguments = null,
        bool areCustomArgumentsAllowed = false,
        DockerSpec? dockerSpec = null,
        TimeSpan? timeout = null ) : base(
        id,
        name,
        $"{checkoutDirectory}/Build.ps1",
        GetScriptArguments( id, command, arguments, areCustomArgumentsAllowed, timeout ),
        dockerSpec )
    {
        if ( areCustomArgumentsAllowed )
        {
            this.AddParameter(
                new TextBuildConfigurationParameter(
                    GetCustomArgumentsParameterName( id ),
                    $"{this.ScriptPath} Arguments",
                    $"Arguments to append to the '{name}' build step.",
                    allowEmpty: true ) );
        }

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
        bool areCustomArgumentsAllowed,
        TimeSpan? timeout )
    {
        var args = $"{command}{(arguments == null ? "" : $" {arguments}")}{(!areCustomArgumentsAllowed ? "" : $" %{GetCustomArgumentsParameterName( id )}%")}";

        if ( timeout != null )
        {
            args += $" --timeout %{GetTimeoutParameterName( id )}%";
        }

        return args;
    }
}