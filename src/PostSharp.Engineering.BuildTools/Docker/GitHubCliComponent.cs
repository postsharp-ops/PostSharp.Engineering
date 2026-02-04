// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

[PublicAPI]
public sealed class GitHubCliComponent : ContainerComponent
{
    public override string Name => "Install GitHub CLI";

    public override ContainerComponentKind Kind => ContainerComponentKind.GitHubCli;

    public override void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem )
    {
        writer.WriteLine(
            """
            RUN Invoke-WebRequest -Uri https://github.com/cli/cli/releases/download/v2.63.2/gh_2.63.2_windows_amd64.msi -OutFile gh.msi; `
                $process = Start-Process msiexec.exe -Wait -PassThru -ArgumentList '/I gh.msi /quiet'; `
                if ($process.ExitCode -ne 0) { exit $process.ExitCode }; `
                Remove-Item gh.msi

            ENV PATH="C:\Program Files\GitHub CLI;${PATH}"
            """ );
    }
}