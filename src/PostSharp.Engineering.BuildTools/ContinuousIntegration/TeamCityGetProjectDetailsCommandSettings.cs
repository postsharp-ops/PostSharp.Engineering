// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

[PublicAPI]
public class TeamCityGetProjectDetailsCommandSettings : CommonCommandSettings
{
    [Description( "The ID of the project." )]
    [CommandArgument( 0, "<id>" )]
    public string Id { get; init; } = null!;
}