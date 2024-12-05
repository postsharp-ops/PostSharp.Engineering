// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.ContinuousIntegration;

[PublicAPI]
public class TeamCityCreateThisVcsRootCommandSettings : CommonCommandSettings
{
    [Description( "The id of the project where the VCS root gets created. Skip for the root project." )]
    [CommandArgument( 0, "[projectId]" )]
    public string? ProjectId { get; init; }
}