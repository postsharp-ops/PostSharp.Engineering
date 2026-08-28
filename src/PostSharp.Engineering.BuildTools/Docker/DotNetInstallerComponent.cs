// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

[PublicAPI]
public sealed class DotNetInstallerComponent : ContainerComponent
{
    public override string Name => "Download .NET Installer";

    public override ContainerComponentKind Kind => ContainerComponentKind.DotNetInstaller;

    public override void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem )
    {
        if ( operatingSystem == ContainerOperatingSystem.Linux )
        {
            writer.WriteLine(
                """
                RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /usr/local/bin/dotnet-install.sh \
                    && chmod +x /usr/local/bin/dotnet-install.sh

                ENV DOTNET_ROOT=/usr/share/dotnet
                ENV PATH="${DOTNET_ROOT}:${PATH}"
                """ );
        }
        else
        {
            writer.WriteLine(
                """
                RUN Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1

                # Add .NET to PATH using ENV directive (persists across shell switches)
                ENV PATH="C:\Program Files\dotnet;${PATH}"
                """ );
        }
    }
}