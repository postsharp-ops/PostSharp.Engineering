// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Build;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Tools.GitHub;

[PublicAPI]
internal class GetGitHubAppTokenSettings : BaseBuildSettings
{
    [Description(
        "A repository the token must reach, as owner/name. Repeatable, including across owners. Defaults to the repository of the current product." )]
    [CommandOption( "-r|--repository" )]
    public string[] Repositories { get; protected set; } = [];

    [Description( "The file receiving the NAME=VALUE lines. Defaults to artifacts/github-app-tokens.env." )]
    [CommandOption( "-o|--output" )]
    public string? OutputFile { get; protected set; }

    [Description( "Appends to the output file instead of replacing it, so that several calls can build one file." )]
    [CommandOption( "--append" )]
    public bool Append { get; protected set; }
}
