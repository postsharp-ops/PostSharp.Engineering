// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.IO;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.Model.BuildSteps;

public class TeamCityPowerShellBuildStep : TeamCityBuildStep
{
    public string Id { get; }

    public string Name { get; }

    public string ScriptPath { get; }

    public string ScriptArguments { get; }

    public string? WorkingDirectory { get; init; }

    public TimeSpan? TimeOut { get; init; }

    public TeamCityPowerShellBuildStep( string id, string name, string scriptPath, string scriptArguments )
    {
        this.Id = id;
        this.Name = name;
        this.ScriptPath = scriptPath;
        this.ScriptArguments = scriptArguments;
    }

    public override string GenerateTeamCityCode()
    {
        var parameters = this.TimeOut != null ? $"param(\"TimeOut\", \"{this.TimeOut}\")" : "";

        return $@"        powerShell {{
            name = ""{this.Name}""
            id = ""{this.Id}""{(this.WorkingDirectory == null ? "" : $@"
            workingDir = ""{this.WorkingDirectory.Replace( Path.DirectorySeparatorChar, '/' )}""")}
            {parameters}
            scriptMode = file {{
                path = ""{this.ScriptPath}""
            }}
            noProfile = false
            scriptArgs = ""{this.ScriptArguments}""
        }}";
    }
}