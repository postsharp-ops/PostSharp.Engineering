// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Build.Testing
{
    /// <summary>
    /// Settings for <see cref="AnalyzeCoverageCommand"/>.
    /// </summary>
    public class AnalyzeCoverageCommandSettings : CommandSettings
    {
        [CommandArgument( 0, "<path>" )]
        [Description( "Path to the OpenCover xml file" )]
        public string Path { get; init; } = null!;
    }
}