// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

public class GitComponent : ContainerComponent
{
    public override string Name => "Install Git";

    public override ContainerComponentKind Kind => ContainerComponentKind.Git;

    public override void WriteDockerfile( TextWriter writer )
    {
        writer.WriteLine(
            """
            RUN Invoke-WebRequest -Uri https://github.com/git-for-windows/git/releases/download/v2.50.0.windows.1/MinGit-2.50.0-64-bit.zip -OutFile MinGit.zip; `
                Expand-Archive c:\\MinGit.zip -DestinationPath C:\\git; `
                Remove-Item C:\\MinGit.zip; `
                $pathsToAdd = @('C:\git\cmd', 'C:\git\bin', 'C:\git\usr\bin'); `
                $newPath = [Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' + ($pathsToAdd -join ';'); `
                [Environment]::SetEnvironmentVariable('PATH', $newPath, 'Machine');
                
            RUN "C:\Git\cmd\git.exe" config --system core.longpaths true
            """ );
    }
}