// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;

/// <summary>
/// Represents a TeamCity <c>SSH Exec</c> build step that runs a command on a target machine over SSH. Authentication
/// uses the key loaded by the <c>SSH Agent</c> build feature (see <see cref="TeamCityBuildConfiguration.IsSshAgentRequired"/>).
/// </summary>
internal class SshExecBuildStep : BuildStep
{
    public string Id { get; }

    public string Name { get; }

    /// <summary>
    /// Gets the command(s) executed in the remote shell. Kept on a single line: <see cref="KotlinHelper.EscapeString"/>
    /// escapes <c>$</c>, <c>"</c> and <c>\</c> but not newlines.
    /// </summary>
    public string Commands { get; }

    /// <summary>
    /// Gets the target host name (or IP address).
    /// </summary>
    public string TargetUrl { get; }

    public string UserName { get; }

    public int Port { get; }

    public SshExecBuildStep( string id, string name, string commands, string targetUrl, string userName, int port )
        : base( null )
    {
        this.Id = id;
        this.Name = name;
        this.Commands = commands;
        this.TargetUrl = targetUrl;
        this.UserName = userName;
        this.Port = port;
    }

    public override string GenerateTeamCityCode()
    {
        var portCode = this.Port == 22
            ? ""
            : $@"
            port = {this.Port}";

        return $@"        sshExec {{
            name = ""{KotlinHelper.EscapeString( this.Name )}""
            id = ""{this.Id}""
            commands = ""{KotlinHelper.EscapeString( this.Commands )}""
            targetUrl = ""{KotlinHelper.EscapeString( this.TargetUrl )}""{portCode}
            authMethod = sshAgent {{
                username = ""{KotlinHelper.EscapeString( this.UserName )}""
            }}
        }}";
    }
}
