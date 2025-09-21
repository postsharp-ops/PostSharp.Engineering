// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

[PublicAPI]
public sealed class AzureCliComponent : ContainerComponent
{
    public override string Name => "Install Azure CLI";

    public override ContainerComponentKind Kind => ContainerComponentKind.AzureCli;

    public override void WriteDockerfile( TextWriter writer )
    {
        writer.WriteLine(
            """
            RUN Invoke-WebRequest -Uri https://aka.ms/installazurecliwindowsx64 -OutFile AzureCLI.msi; `
                $process = Start-Process msiexec.exe -Wait -PassThru -ArgumentList '/I AzureCLI.msi /quiet'; `
                if ($process.ExitCode -ne 0) { exit $process.ExitCode }; `
                Remove-Item AzureCLI.msi
            """ );
    }
}