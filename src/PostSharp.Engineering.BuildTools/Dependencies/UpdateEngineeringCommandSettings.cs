using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Dependencies;

[UsedImplicitly]
internal class UpdateEngineeringCommandSettings : CommonCommandSettings
{
    [Description( "Repeat until a new version is discovered." )]
    [CommandOption( "--repeat" )]
    public bool Repeat { get; init; }
}