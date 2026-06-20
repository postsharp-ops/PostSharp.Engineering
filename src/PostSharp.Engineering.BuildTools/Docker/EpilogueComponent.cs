// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Utilities;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

internal class EpilogueComponent : ContainerComponent
{
    public override string Name => "Epilogue";

    public override ContainerComponentKind Kind => ContainerComponentKind.Epilogue;

    public override void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem )
    {
        // Note: bind-mount directories are NOT created here. They are machine-specific, so DockerBuild.ps1
        // creates them in a thin local "boot" image layered over the resolved chain image (New-BootImage),
        // keeping the chain images clean, shareable and free of the host's mount set.
        writer.WriteLine(
            """
            # Configure .NET SDK
            ENV DOTNET_NOLOGO=1
            """ );
    }
}