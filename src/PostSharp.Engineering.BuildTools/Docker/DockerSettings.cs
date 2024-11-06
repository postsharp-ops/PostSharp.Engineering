// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Docker;

[PublicAPI]
public class DockerSettings : BuildSettings
{
    [Description( "Does not execute ./dependencies/ConfigureContainer.ps1. Only useful with `docker interactive`." )]
    [CommandOption( "--docker-no-configure" )]
    public bool SkipConfigure { get; set; }

    [Description( "Sets the name of the Docker image" )]
    [CommandOption( "--docker-image" )]
    public string? ImageName { get; set; }
}