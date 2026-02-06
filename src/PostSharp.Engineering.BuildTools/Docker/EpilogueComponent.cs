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
        writer.WriteLine(
            """
            # Create directories for mountpoints
            ARG MOUNTPOINTS
            RUN if ($env:MOUNTPOINTS) { `
                    $mounts = $env:MOUNTPOINTS -split ';'; `
                    foreach ($dir in $mounts) { `
                        if ($dir) { `
                            Write-Host "Creating directory $dir`."; `
                            New-Item -ItemType Directory -Path $dir -Force | Out-Null; `
                        } `
                    } `
                }

            # Configure .NET SDK
            ENV DOTNET_NOLOGO=1
            """ );
    }
}