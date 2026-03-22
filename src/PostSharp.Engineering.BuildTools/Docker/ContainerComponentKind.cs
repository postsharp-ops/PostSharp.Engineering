// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Docker;

public enum ContainerComponentKind
{
    // Order matters and determines the order of execution in Dockerfile.
    Prolog,
    Git,
    Powershell,
    DotNetInstaller,
    DotNet,
    DotNetDump,
    VsBuildTools,
    Chocolatey,
    NodeJs,
    Python,
    Gulp,
    GitHubCli,
    AzureCli,
    AzureArtifactsCredentialProvider,
    Timestamp,
    Claude,
    ClaudeAddIns,
    Epilogue
}