// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Build;

[PublicAPI]
public class BumpSettings : CommonCommandSettings
{
    [Description( "Ignore any previous bump commit." )]
    [CommandOption( "--override" )]
    public bool OverridePreviousBump { get; init; }
}