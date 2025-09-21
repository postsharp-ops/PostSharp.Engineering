// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Tools.TeamCity;

[UsedImplicitly]
public class TeamCityCreateProjectCommandSettings : CommonCommandSettings
{
    [Description( "The name of the new project." )]
    [CommandArgument( 0, "<name>" )]
    public string Name { get; init; } = null!;

    [Description( "The ID of the new project." )]
    [CommandArgument( 1, "<id>" )]
    public string Id { get; init; } = null!;

    [Description( "The id of the parent project. Skip for the root project." )]
    [CommandArgument( 2, "[parentId]" )]
    public string? ParentId { get; init; }

    [Description( "The id of the VCS root used to set versioned settings." )]
    [CommandArgument( 2, "[vcsRootId]" )]
    public string? VcsRootId { get; init; }
}