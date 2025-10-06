// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

public class ChocolateyComponent : ContainerComponent
{
    public override string Name => "Install Chocolatey";

    public override ContainerComponentKind Kind => ContainerComponentKind.Chocolatey;

    public override void WriteDockerfile( TextWriter writer )
    {
        writer.WriteLine(
            """
            RUN powershell -c "irm https://community.chocolatey.org/install.ps1|iex" ` 
            $pathsToAdd = @('C:\ProgramData\chocolatey\bin'); `
            $newPath = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + ($pathsToAdd -join ';'); `
            [Environment]::SetEnvironmentVariable('PATH', $newPath, 'Machine'); `
            & C:\ProgramData\chocolatey\bin\choco.exe feature enable -n allowGlobalConfirmation
            """ );
    }
}