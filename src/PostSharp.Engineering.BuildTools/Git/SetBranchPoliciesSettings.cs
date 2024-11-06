// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Git;

[PublicAPI]
internal class SetBranchPoliciesSettings : BaseBuildSettings
{
    [Description( "Prints the command line, but does not execute it" )]
    [CommandOption( "--dry" )]
    public bool Dry { get; protected set; }
}