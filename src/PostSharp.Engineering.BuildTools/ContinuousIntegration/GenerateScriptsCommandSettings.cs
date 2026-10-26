// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

internal class GenerateScriptsCommandSettings : CommonCommandSettings
{
    [Description(
        "Do not restore the solutions that contain test applications. The packages of each test archive are taken from the existing "
        + "list of the test archives instead of the restore graph, so the product does not need to be built." )]
    [CommandOption( "--no-restore" )]
    public bool NoRestore { get; init; }
}
