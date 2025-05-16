// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.BillOfMaterials;

[UsedImplicitly]
internal class GenerateThirdPartyNoticesCommandSettings : CommonCommandSettings
{
    [Description( "Only generates the list of dependencies, but do not fetch and append the license notices." )]
    [CommandOption( "--list-only" )]
    public bool ListOnly { get; protected set; }
}