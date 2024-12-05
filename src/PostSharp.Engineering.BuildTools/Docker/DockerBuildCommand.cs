// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;

namespace PostSharp.Engineering.BuildTools.Docker;

[UsedImplicitly]
public class DockerBuildCommand : DockerRunCommand
{
    protected override string GetCommand( DockerSettings settings, DockerImage image )
    {
        var dockerSettings = settings.WithoutLogo();

        if ( settings.SkipConfigure )
        {
            return $"{image.PowerShellCommand} -c \"./Build.ps1 build {dockerSettings};\"";
        }
        else
        {
            return $"{image.PowerShellCommand} -c \" ./dependencies/ConfigureContainer.ps1; ./Build.ps1 build {dockerSettings};\"";
        }
    }
}