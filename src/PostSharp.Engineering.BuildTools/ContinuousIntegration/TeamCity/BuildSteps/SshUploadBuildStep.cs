// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;

/// <summary>
/// Represents a TeamCity <c>SSH Upload</c> build step that transfers files to a target machine using SCP. Authentication
/// uses the key loaded by the <c>SSH Agent</c> build feature (see <see cref="TeamCityBuildConfiguration.IsSshAgentRequired"/>).
/// </summary>
internal class SshUploadBuildStep : BuildStep
{
    public string Id { get; }

    public string Name { get; }

    /// <summary>
    /// Gets the source path (relative to the build checkout directory, Ant-style wildcards allowed) of the files to upload.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// Gets the target URL in the form <c>host:path/to/target/folder</c>.
    /// </summary>
    public string TargetUrl { get; }

    public string UserName { get; }

    public int Port { get; }

    public SshUploadBuildStep( string id, string name, string sourcePath, string targetUrl, string userName, int port )
        : base( null )
    {
        this.Id = id;
        this.Name = name;
        this.SourcePath = sourcePath;
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

        return $@"        sshUpload {{
            name = ""{KotlinHelper.EscapeString( this.Name )}""
            id = ""{this.Id}""
            transportProtocol = SSHUpload.TransportProtocol.SCP
            sourcePath = ""{KotlinHelper.EscapeString( this.SourcePath )}""
            targetUrl = ""{KotlinHelper.EscapeString( this.TargetUrl )}""{portCode}
            authMethod = sshAgent {{
                username = ""{KotlinHelper.EscapeString( this.UserName )}""
            }}
        }}";
    }
}
