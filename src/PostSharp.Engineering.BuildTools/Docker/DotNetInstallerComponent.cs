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
        writer.WriteLine(
            """
            RUN Invoke-WebRequest -Uri https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1

            # Add .NET to PATH using ENV directive (persists across shell switches)
            ENV PATH="C:\Program Files\dotnet;${PATH}"
            """ );
    }
}