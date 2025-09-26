// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Tools.Git;

[PublicAPI]
internal class SetDefaultBranchSettings : BaseBuildSettings
{
    [Description(
        "The branch name to be set as default. When omitted, the default branch is selected according to the current branch. Examples: 'develop/2023.0'" )]
    [CommandArgument( 0, "[default-branch]" )]
    public string? DefaultBranch { get; protected set; }

    [Description( "Prints the command line, but does not execute it" )]
    [CommandOption( "--dry" )]
    public bool Dry { get; protected set; }
}