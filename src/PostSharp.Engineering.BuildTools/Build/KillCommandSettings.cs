// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Build
{
    /// <summary>
    /// Settings of <see cref="KillCommand"/>.
    /// </summary>
    [PublicAPI]
    public class KillCommandSettings : CommonCommandSettings
    {
        [Description( "Prints the process that would be killed, but does not kill it" )]
        [CommandOption( "--dry" )]
        public bool Dry { get; protected set; }
    }
}