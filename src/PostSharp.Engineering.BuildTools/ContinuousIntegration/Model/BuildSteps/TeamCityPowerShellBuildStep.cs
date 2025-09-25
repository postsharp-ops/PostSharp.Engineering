// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.Arguments;
using System;
using System.Globalization;
using System.IO;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;

public class TeamCityPowerShellBuildStep : TeamCityBuildStep
{
    private readonly TimeSpan? _timeout;

    public string Id { get; }

    public string Name { get; }

    public string ScriptPath { get; }

    public string ScriptArguments { get; }

    public string? WorkingDirectory { get; init; }

    private string TimeoutParameterName => $"{this.Id}.Timeout";

    public TeamCityPowerShellBuildStep( string id, string name, string scriptPath, string scriptArguments, TimeSpan? timeout = null )
    {
        this._timeout = timeout;
        this.Id = id;
        this.Name = name;
        this.ScriptPath = scriptPath;
        this.ScriptArguments = scriptArguments;

        timeout ??= TimeSpan.FromMinutes( 15 );

        this.AddParameter(
            new TeamCityBuildConfigurationParameter( this.TimeoutParameterName, timeout.Value.TotalMinutes.ToString( CultureInfo.InvariantCulture ) ) );
    }

    public override string GenerateTeamCityCode()
    {
        var timeout = "";

        if ( this._timeout.HasValue )
        {
            timeout =
                $"executionTimeoutMin = \"%{this.TimeoutParameterName}%.toInt()\" ";
        }

        return $@"        powerShell {{
            name = ""{this.Name}""
            id = ""{this.Id}""{(this.WorkingDirectory == null ? "" : $@"
            workingDir = ""{this.WorkingDirectory.Replace( Path.DirectorySeparatorChar, '/' )}""")}
            scriptMode = file {{
                path = ""{this.ScriptPath}""
            }}
            noProfile = false
            scriptArgs = ""{this.ScriptArguments}""
            {timeout}
        }}";
    }
}