// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.CodeStyle;

/// <summary>
/// Settings of <see cref="PullCodeStyleCommand"/>.
/// </summary>
[PublicAPI]
internal class PullCodeStyleSettings : CodeStyleSettings
{
    [Description( "Name of the branch. The default is 'develop' for push and 'master' for pull." )]
    [CommandOption( "-b|--branch" )]
    public string? Branch { get; init; }
}