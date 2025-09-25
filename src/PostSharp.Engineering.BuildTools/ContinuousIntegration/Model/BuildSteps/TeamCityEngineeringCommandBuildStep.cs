// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.Arguments;
using PostSharp.Engineering.BuildTools.Docker;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;

public class TeamCityEngineeringCommandBuildStep : TeamCityPowerShellBuildStep
{
    private readonly DockerSpec? _dockerSpec;

    private static string GetCustomArgumentsParameterName( string id ) => $"{id}.Arguments";

    public TeamCityEngineeringCommandBuildStep(
        string id,
        string name,
        string command,
        string? arguments = null,
        bool areCustomArgumentsAllowed = false,
        DockerSpec? dockerSpec = null,
        TimeSpan? timeout = null ) : base(
        id,
        name,
        dockerSpec != null ? "DockerBuild.ps1" : "Build.ps1",
        GetScriptArguments( id, command, arguments, areCustomArgumentsAllowed, dockerSpec ),
        timeout )
    {
        this._dockerSpec = dockerSpec;

        if ( areCustomArgumentsAllowed )
        {
            this.AddParameter(
                new TeamCityTextBuildConfigurationParameter(
                    GetCustomArgumentsParameterName( id ),
                    $"{this.ScriptPath} Arguments",
                    $"Arguments to append to the '{name}' build step.",
                    allowEmpty: true ) );
        }
    }

    private static string GetScriptArguments( string id, string command, string? arguments, bool areCustomArgumentsAllowed, DockerSpec? dockerSpec )
    {
        var args = $"{command}{(arguments == null ? "" : $" {arguments}")}{(!areCustomArgumentsAllowed ? "" : $" %{GetCustomArgumentsParameterName( id )}%")}";

        if ( dockerSpec != null )
        {
            args = $"-ImageName {dockerSpec.ImageName} -NoBuildImage " + args;
        }

        return args;
    }

    public override void InsertPrerequisites( IReadOnlyList<TeamCityBuildStep> previousSteps, Action<TeamCityBuildStep> addStep )
    {
        base.InsertPrerequisites( previousSteps, addStep );

        if ( this._dockerSpec != null )
        {
            var prepareImageStep = previousSteps
                .OfType<TeamCityEngineeringPrepareImageBuildStep>()
                .SingleOrDefault( i => i.DockerSpec.ImageName == this._dockerSpec.ImageName );

            if ( prepareImageStep == null )
            {
                addStep( new TeamCityEngineeringPrepareImageBuildStep( "PrepareImage", this._dockerSpec ) );
            }
        }
    }
}