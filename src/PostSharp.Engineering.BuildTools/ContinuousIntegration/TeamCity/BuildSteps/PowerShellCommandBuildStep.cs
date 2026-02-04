// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Docker;
using System.IO;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;

/// <summary>
/// Represents a PowerShell build step that executes a command directly (not a script file).
/// </summary>
internal class PowerShellCommandBuildStep : BuildStep
{
    public string Id { get; }

    public string Name { get; }

    public string Command { get; }

    public string? WorkingDirectory { get; init; }

    public PowerShellCommandBuildStep(
        string id,
        string name,
        string command,
        DockerSpec? dockerSpec ) : base( dockerSpec )
    {
        this.Id = id;
        this.Name = name;
        this.Command = command;
    }

    public override string GenerateTeamCityCode()
    {
        return $@"        powerShell {{
            name = ""{KotlinHelper.EscapeString( this.Name )}""
            id = ""{this.Id}""
            edition = PowerShellStep.Edition.Core{(this.WorkingDirectory == null ? "" : $@"
            workingDir = ""{this.WorkingDirectory.Replace( Path.DirectorySeparatorChar, '/' )}""")}
            scriptMode = script {{
                content = ""{KotlinHelper.EscapeString( this.Command )}""
            }}
            noProfile = false
        }}";
    }
}