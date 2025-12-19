// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Dependencies;

[UsedImplicitly]
internal class UpdateEngineeringCommandSettings : CommonCommandSettings
{
    [Description( "Retry until a new version is discovered." )]
    [CommandOption( "--retry" )]
    public bool Retry { get; init; }
}