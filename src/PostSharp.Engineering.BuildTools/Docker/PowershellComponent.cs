// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

public class PowershellComponent : ContainerComponent
{
    private const string _version = "7.5.2";

    private readonly ContainerArchitecture _architecture;

    public PowershellComponent( ContainerArchitecture architecture = ContainerArchitecture.X64 )
    {
        this._architecture = architecture;
    }

    public override string Name => "Install PowerShell 7";

    public override ContainerComponentKind Kind => ContainerComponentKind.Powershell;

    public override void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem )
    {
        if ( operatingSystem == ContainerOperatingSystem.Linux )
        {
            // Microsoft ships a .deb for x64 only; on ARM64 the binary archive is the only option.
            writer.WriteLine(
                this._architecture == ContainerArchitecture.Arm64
                    ? $"""
                       RUN wget -q https://github.com/PowerShell/PowerShell/releases/download/v{_version}/powershell-{_version}-linux-arm64.tar.gz \
                           && mkdir -p /opt/microsoft/powershell/7 \
                           && tar zxf powershell-{_version}-linux-arm64.tar.gz -C /opt/microsoft/powershell/7 \
                           && chmod +x /opt/microsoft/powershell/7/pwsh \
                           && ln -s /opt/microsoft/powershell/7/pwsh /usr/bin/pwsh \
                           && rm powershell-{_version}-linux-arm64.tar.gz
                       """
                    : $"""
                       RUN wget -q https://github.com/PowerShell/PowerShell/releases/download/v{_version}/powershell_{_version}-1.deb_amd64.deb \
                           && dpkg -i powershell_{_version}-1.deb_amd64.deb \
                           && rm powershell_{_version}-1.deb_amd64.deb
                       """ );
        }
        else
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
}