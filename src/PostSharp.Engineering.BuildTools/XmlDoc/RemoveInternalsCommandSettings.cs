// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.XmlDoc;

[PublicAPI]
public class RemoveInternalsCommandSettings : CommonCommandSettings
{
    [Description( "Path to the xml file. The dll is assumed to be next to it." )]
    [CommandArgument( 0, "<xml-path>" )]
    public string XmlPath { get; init; } = null!;

    [Description( "Path to the csproj file." )]
    [CommandArgument( 1, "<project-path>" )]
    public string ProjectPath { get; init; } = null!;

    [Description( "Does not save the file. Use with --verbose." )]
    [CommandOption( "--dry" )]
    public bool Dry { get; init; }
}