// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

public class PowershellComponent : ContainerComponent
{
    public override string Name => "Install PowerShell 7";

    public override ContainerComponentKind Kind => ContainerComponentKind.Powershell;

    public override void WriteDockerfile( TextWriter writer )
    {
        writer.WriteLine(
            """
            RUN Invoke-WebRequest -Uri https://github.com/PowerShell/PowerShell/releases/download/v7.5.2/PowerShell-7.5.2-win-x64.msi -OutFile PowerShell.msi; `
                $process = Start-Process msiexec.exe -Wait -PassThru -ArgumentList '/I PowerShell.msi /quiet'; `
                if ($process.ExitCode -ne 0) { exit $process.ExitCode }; `
                Remove-Item PowerShell.msi

            ENV PATH="C:\Program Files\PowerShell\7;${PATH}"
            """ );
    }
}