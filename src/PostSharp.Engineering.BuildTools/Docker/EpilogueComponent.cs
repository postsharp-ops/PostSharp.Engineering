// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Utilities;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

internal class EpilogueComponent : ContainerComponent
{
    public override string Name => "Epilogue";

    public override ContainerComponentKind Kind => ContainerComponentKind.Epilogue;

    public override void WriteDockerfile( TextWriter writer )
    {
        writer.WriteLine(
            """
            # Create docker-context directory for build scripts
            RUN New-Item -ItemType Directory -Path c:\docker-context -Force | Out-Null

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

            # Import environment variables
            COPY ReadEnvironmentVariables.ps1 c:\docker-context\ReadEnvironmentVariables.ps1
            COPY .g/env.g.json c:\docker-context\env.g.json
            RUN c:\docker-context\ReadEnvironmentVariables.ps1 c:\docker-context\env.g.json

            # Copy Init.g.ps1 placeholder (drive mappings handled inline in docker run)
            COPY .g/Init.g.ps1 c:\docker-context\Init.g.ps1

            # Configure .NET SDK
            ENV DOTNET_NOLOGO=1
            """ );
    }

    public override void PopulateContextDirectory( BuildContext context, string directory )
    {
        EmbeddedResourceHelper.ExtractScript( context, "ReadEnvironmentVariables.ps1", directory );
    }
}