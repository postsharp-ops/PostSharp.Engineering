// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Build.Swapping
{
    /// <summary>
    /// Settings of <see cref="SwapCommand"/>.
    /// </summary>
    public class SwapSettings : CommonCommandSettings
    {
        [Description( "Sets the build configuration (Debug | Release | Public) to swap" )]
        [CommandOption( "-c|--configuration" )]
        public BuildConfiguration BuildConfiguration { get; set; }

        [Description( "Prints the command line, but does not execute it" )]
        [CommandOption( "--dry" )]
        public bool Dry { get; init; }
    }
}