// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

namespace PostSharp.Engineering.BuildTools.Docker;

public enum ContainerComponentKind
{
    // Order matters and determines the order of execution in Dockerfile.
    //
    // The .NET SDK components (DotNetInstaller, DotNet, DotNetDump) are placed as late as possible, immediately
    // before Timestamp, rather than next to Git and Powershell where they used to sit. A Docker layer is
    // invalidated by any change to the layer that produced it or to any earlier layer, so a component placed
    // early in this list invalidates every later component whenever it changes, even when the two are unrelated.
    // The pinned SDK version changes far more often than Git, PowerShell, the Azure CLI or the other tools that
    // now precede it, so keeping the SDK late means a version bump only invalidates the SDK layer itself and the
    // few components still following it, instead of rebuilding unrelated tools that did not change.
    Prolog,
    Git,
    Powershell,
    VsBuildTools,
    Chocolatey,
    NodeJs,
    Python,
    Gulp,
    GitHubCli,
    AzureCli,
    AzureArtifactsCredentialProvider,
    DotNetInstaller,
    DotNet,
    DotNetDump,
    Timestamp,
    Claude,
    ClaudeAddIns,
    Epilogue
}