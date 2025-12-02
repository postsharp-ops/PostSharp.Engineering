using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.CodeStyle;

internal class ResharperCommandSettings : CommonCommandSettings
{
    [Description( "Do not build the product before executing the command." )]
    [CommandOption( "--no-build" )]
    public bool NoBuild { get; protected set; }

}