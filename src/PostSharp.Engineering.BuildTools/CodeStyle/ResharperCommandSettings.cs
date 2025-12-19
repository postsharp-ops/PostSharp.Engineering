// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.CodeStyle;

internal class ResharperCommandSettings : CommonCommandSettings
{
    [Description( "Do not build the product before executing the command." )]
    [CommandOption( "--no-build" )]
    public bool NoBuild { get; protected set; }
}