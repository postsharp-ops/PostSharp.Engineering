// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Build.Publishing;

[PublicAPI]
public class PublishSettings : BuildSettings
{
    [Description( "Prints the command line, but does not execute it" )]
    [CommandOption( "--dry" )]
    public bool Dry { get; protected set; }

    [Description( "Write the files (unless --dry is used) but do not commit them." )]
    [CommandOption( "--no-commit" )]
    public bool NoCommit { get; init; }

    [Description( "Avoids check of the current branch" )]
    [CommandOption( "--standalone" )]
    public bool IsStandalone { get; protected set; }

    [Description( "Name of the deployment to publish. Required when the configuration defines more than one deployment." )]
    [CommandOption( "--deployment" )]
    public string? Deployment { get; init; }
}