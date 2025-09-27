// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Docker;
using System.IO;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;

internal class PowerShellBuildStep : BuildStep
{
    public string Id { get; }

    public string Name { get; }

    public string ScriptPath { get; }

    public string ScriptArguments { get; }

    public string? WorkingDirectory { get; init; }

    public PowerShellBuildStep( string id, string name, string scriptPath, string scriptArguments, DockerSpec? dockerSpec ) : base( dockerSpec )
    {
        this.Id = id;
        this.Name = name;

        if ( dockerSpec == null )
        {
            this.ScriptPath = scriptPath;
            this.ScriptArguments = scriptArguments;
        }
        else
        {
            this.ScriptPath = "DockerBuild.ps1";
            this.ScriptArguments = $"-Script {scriptPath} -ImageName {dockerSpec.ImageName} -NoBuildImage {scriptArguments}";
        }
    }

    public override string GenerateTeamCityCode()
    {
        return $@"        powerShell {{
            name = ""{this.Name}""
            id = ""{this.Id}""{(this.WorkingDirectory == null ? "" : $@"
            workingDir = ""{this.WorkingDirectory.Replace( Path.DirectorySeparatorChar, '/' )}""")}
            scriptMode = file {{
                path = ""{this.ScriptPath}""
            }}
            noProfile = false
            scriptArgs = ""{this.ScriptArguments}""
        }}";
    }
}