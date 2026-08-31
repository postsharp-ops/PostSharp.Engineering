// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build.Publishing;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.Model;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.BuildSteps;
using PostSharp.Engineering.BuildTools.ContinuousIntegration.TeamCity.Generation;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace PostSharp.Engineering.BuildTools.Tests;

public class SshDeploymentTests
{
    [Fact]
    public void SshPublisher_IsAPublisherWithDeploymentProperties()
    {
        var publisher = new SshPublisher( "host1", "deployer", "C:/deploy" ) { ArchivePattern = "MyApp-*.zip" };

        Assert.IsAssignableFrom<Publisher>( publisher );
        Assert.Equal( "host1", publisher.HostName );
        Assert.Equal( "deployer", publisher.UserName );
        Assert.Equal( "C:/deploy", publisher.RemoteDirectory );
        Assert.Equal( "MyApp-*.zip", publisher.ArchivePattern );

        // Defaults.
        Assert.Equal( 22, publisher.Port );
        Assert.Equal( "PostSharp.Engineering", publisher.SshKeyName );
    }

    // The default bootstrapper must be a base64 -EncodedCommand so a target whose default SSH shell is PowerShell
    // cannot expand the script's $ variables before it runs. A -Command "…$var…" string would be mangled by that
    // outer shell; a base64 payload (no $, quotes, or spaces) passes through unchanged.
    [Fact]
    public void DefaultBootstrapper_IsBase64EncodedAndShellSafe()
    {
        var command = TeamCitySettingsFile.GetDefaultBootstrapperCommand( "C:/Deploy/App", "App.*.zip" );

        const string prefix = "pwsh -NoProfile -ExecutionPolicy Bypass -EncodedCommand ";
        Assert.StartsWith( prefix, command, StringComparison.Ordinal );

        // The payload token must be pure base64: no $, quotes, or spaces, so no outer shell can mangle it.
        var encoded = command.Substring( prefix.Length );
        Assert.Matches( "^[A-Za-z0-9+/]+={0,2}$", encoded );
        Assert.DoesNotContain( "$", command, StringComparison.Ordinal );
        Assert.DoesNotContain( "\"", command, StringComparison.Ordinal );
        Assert.DoesNotContain( " ", encoded, StringComparison.Ordinal );

        // Decoding the UTF-16LE payload yields the real extract-and-run script, including the "no archive" guard.
        var script = Encoding.Unicode.GetString( Convert.FromBase64String( encoded ) );
        Assert.Contains( "$ErrorActionPreference = 'Stop'", script, StringComparison.Ordinal );
        Assert.Contains( "$directory = 'C:/Deploy/App'", script, StringComparison.Ordinal );
        Assert.Contains( "-Filter 'App.*.zip'", script, StringComparison.Ordinal );
        Assert.Contains( "if (-not $archive) { throw", script, StringComparison.Ordinal );
        Assert.Contains( "Expand-Archive", script, StringComparison.Ordinal );
        Assert.Contains( "deploy.ps1", script, StringComparison.Ordinal );
    }

    [Fact]
    public void SshUpload_GeneratesScpRunnerWithSshAgentAuth()
    {
        var step = new SshUploadBuildStep(
            "ScpUpload_0",
            "SCP upload to deploy.example.com",
            "artifacts/publish/private/*.zip",
            "deploy.example.com:C:/deploy/incoming",
            "deployer",
            22 );

        var code = step.GenerateTeamCityCode();

        Assert.Contains( "sshUpload {", code, StringComparison.Ordinal );
        Assert.Contains( "transportProtocol = SSHUpload.TransportProtocol.SCP", code, StringComparison.Ordinal );
        Assert.Contains( "sourcePath = \"artifacts/publish/private/*.zip\"", code, StringComparison.Ordinal );
        Assert.Contains( "targetUrl = \"deploy.example.com:C:/deploy/incoming\"", code, StringComparison.Ordinal );
        Assert.Contains( "authMethod = sshAgent {", code, StringComparison.Ordinal );
        Assert.Contains( "username = \"deployer\"", code, StringComparison.Ordinal );

        // The default port must not be emitted.
        Assert.DoesNotContain( "port =", code, StringComparison.Ordinal );
    }

    [Fact]
    public void SshExec_GeneratesExecRunnerWithSshAgentAuth()
    {
        var step = new SshExecBuildStep(
            "SshExec_0",
            "Bootstrap on deploy.example.com",
            "pwsh -NoProfile -Command \"Write-Host hello\"",
            "deploy.example.com",
            "deployer",
            22 );

        var code = step.GenerateTeamCityCode();

        Assert.Contains( "sshExec {", code, StringComparison.Ordinal );
        Assert.Contains( "targetUrl = \"deploy.example.com\"", code, StringComparison.Ordinal );
        Assert.Contains( "authMethod = sshAgent {", code, StringComparison.Ordinal );
        Assert.Contains( "username = \"deployer\"", code, StringComparison.Ordinal );

        // Double quotes inside the command must be escaped for the Kotlin string literal.
        Assert.Contains( "commands = \"pwsh -NoProfile -Command \\\"Write-Host hello\\\"\"", code, StringComparison.Ordinal );
    }

    // Kotlin interpolates '$' in string literals, so a PowerShell '$variable' in the remote command must be emitted as
    // ${'$'}variable, otherwise the generated settings.kts fails to compile.
    [Fact]
    public void SshExec_EscapesDollarSignForKotlin()
    {
        var step = new SshExecBuildStep(
            "SshExec_0",
            "Bootstrap",
            "$ErrorActionPreference='Stop'; $dest='current'",
            "host",
            "deployer",
            22 );

        var code = step.GenerateTeamCityCode();

        Assert.Contains( "${'$'}ErrorActionPreference", code, StringComparison.Ordinal );
        Assert.Contains( "${'$'}dest", code, StringComparison.Ordinal );

        // No raw, unescaped PowerShell variable sigil must survive into the Kotlin literal.
        Assert.DoesNotContain( "\"$ErrorActionPreference", code, StringComparison.Ordinal );
    }

    [Fact]
    public void NonDefaultPort_IsEmitted()
    {
        var upload = new SshUploadBuildStep( "ScpUpload_0", "u", "src", "host:dir", "deployer", 2222 );
        var exec = new SshExecBuildStep( "SshExec_0", "e", "cmd", "host", "deployer", 2222 );

        Assert.Contains( "port = 2222", upload.GenerateTeamCityCode(), StringComparison.Ordinal );
        Assert.Contains( "port = 2222", exec.GenerateTeamCityCode(), StringComparison.Ordinal );
    }

    // Exercises the whole deployment build-config emission: DEPLOYMENT type, the SSH Agent build feature with a custom
    // key name, both SSH runners, and the artifact dependency that pulls the .zip onto the deploy agent.
    [Fact]
    public void DeployConfiguration_EmitsSshAgentFeatureWithCustomKeyAndArtifactDependency()
    {
        var configuration = new TeamCityBuildConfiguration(
            "PublicSshDeployment",
            "Deploy via SSH [Public]",
            "develop/2023.2",
            "SomeVcsId",
            BuildAgentRequirements.Default )
        {
            IsDeployment = true,
            IsSshAgentRequired = true,
            SshAgentKeyName = "MyDeployKey",
            BuildSteps =
            [
                new SshUploadBuildStep(
                    "ScpUpload_0",
                    "SCP upload to host",
                    "artifacts/publish/private/*.zip",
                    "host:C:/deploy/incoming",
                    "deployer",
                    22 ),
                new SshExecBuildStep( "SshExec_0", "Bootstrap on host", "pwsh -NoProfile", "host", "deployer", 22 )
            ],
            SnapshotDependencies =
            [
                new TeamCitySnapshotDependency(
                    "PublicBuild",
                    false,
                    "+:artifacts/publish/private/**/*=>artifacts/publish/private" )
            ]
        };

        var writer = new StringWriter();
        configuration.GenerateTeamcityCode( writer );
        var code = writer.ToString();

        Assert.Contains( "type = Type.DEPLOYMENT", code, StringComparison.Ordinal );
        Assert.Contains( "sshAgent {", code, StringComparison.Ordinal );
        Assert.Contains( "teamcitySshKey = \"MyDeployKey\"", code, StringComparison.Ordinal );
        Assert.Contains( "sshUpload {", code, StringComparison.Ordinal );
        Assert.Contains( "sshExec {", code, StringComparison.Ordinal );
        Assert.Contains( "snapshot(PublicBuild)", code, StringComparison.Ordinal );
        Assert.Contains( "artifacts(PublicBuild)", code, StringComparison.Ordinal );
        Assert.Contains( "artifactRules = \"+:artifacts/publish/private/**/*=>artifacts/publish/private\"", code, StringComparison.Ordinal );
    }

    // Without a custom key name, the conventional PostSharp.Engineering key is used (backward compatibility).
    [Fact]
    public void SshAgentFeature_DefaultsToConventionalKey()
    {
        var configuration = new TeamCityBuildConfiguration(
            "SomeBuild",
            "Some Build",
            "develop/2023.2",
            "SomeVcsId",
            BuildAgentRequirements.Default )
        {
            IsSshAgentRequired = true,
            BuildSteps = [new SshExecBuildStep( "SshExec_0", "e", "cmd", "host", "deployer", 22 )]
        };

        var writer = new StringWriter();
        configuration.GenerateTeamcityCode( writer );

        Assert.Contains( "teamcitySshKey = \"PostSharp.Engineering\"", writer.ToString(), StringComparison.Ordinal );
    }
}
