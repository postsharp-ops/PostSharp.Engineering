// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.IO;

namespace PostSharp.Engineering.BuildTools.Docker;

public class GitComponent : ContainerComponent
{
    public override string Name => "Install Git";

    public override ContainerComponentKind Kind => ContainerComponentKind.Git;

    public override void WriteDockerfile( TextWriter writer, ContainerOperatingSystem operatingSystem )
    {
        // Use full Git for Windows (not MinGit) to include bash.exe which is required by Claude Code
        writer.WriteLine(
            """
            RUN Invoke-WebRequest -Uri https://github.com/git-for-windows/git/releases/download/v2.50.0.windows.1/PortableGit-2.50.0-64-bit.7z.exe -OutFile PortableGit.exe; `
                Start-Process -FilePath .\PortableGit.exe -ArgumentList '-o"C:\git"', '-y' -Wait; `
                Remove-Item PortableGit.exe

            # Add git to PATH using ENV directive (persists across shell switches)
            ENV PATH="C:\git\cmd;C:\git\bin;C:\git\usr\bin;${PATH}"

            RUN git config --system core.longpaths true; git config --system core.autocrlf false

            # Set CLAUDE_CODE_GIT_BASH_PATH for Claude Code
            ENV CLAUDE_CODE_GIT_BASH_PATH=C:\git\bin\bash.exe
            """ );
    }
}