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
            COPY ReadEnvironmentVariables.ps1 c:\ReadEnvironmentVariables.ps1
            COPY env.g.json c:\env.g.json
            RUN c:\ReadEnvironmentVariables.ps1 c:\env.g.json

            # Copy Init.g.ps1 placeholder (drive mappings handled inline in docker run)
            COPY Init.g.ps1 c:\Init.g.ps1

            # Configure NuGet
            ENV NUGET_PACKAGES=c:\packages

            # Configure .NET SDK
            ENV DOTNET_NOLOGO=1
            """ );
    }

    public override void PopulateContextDirectory( BuildContext context, string directory )
    {
        EmbeddedResourceHelper.ExtractScript( context, "ReadEnvironmentVariables.ps1", directory );
    }
}