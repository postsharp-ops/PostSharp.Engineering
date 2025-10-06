// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Docker;

public enum ContainerComponentKind
{
    // Order matters and determines the order of execution in Dockerfile.
    Prolog,
    Git,
    Powershell,
    AzureCli,
    DotNetInstaller,
    DotNet,
    DotNetDump,
    VsBuildTools,
    Chocolatey,
    NodeJs,
    Epilogue
}