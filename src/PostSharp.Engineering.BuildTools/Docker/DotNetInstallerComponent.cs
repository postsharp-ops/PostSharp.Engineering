// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

public class DotNetInstallerComponent : ContainerComponent
{
    public override string Name => "Download .NET Installer";

    public override ContainerComponentKind Kind => ContainerComponentKind.DotNetInstaller;

    public override void WriteDockerfile( StreamWriter writer )
    {
        writer.WriteLine(
            """
            RUN Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1; `
                $pathsToAdd = @('C:\Program Files\dotnet'); `
                $newPath = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + ($pathsToAdd -join ';'); `
                [Environment]::SetEnvironmentVariable('PATH', $newPath, 'Machine'); 
            """ );
    }
}