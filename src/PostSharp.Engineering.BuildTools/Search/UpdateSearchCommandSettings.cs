// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace PostSharp.Engineering.BuildTools.Search;

[UsedImplicitly]
public class UpdateSearchCommandSettings : CommonCommandSettings
{
    [Description(
        "The source (collection selector) to update, matching the 'source' of one of the product's search extensions. "
        + "Optional when the product defines a single search collection." )]
    [CommandArgument( 0, "[source]" )]
    public string? Source { get; init; } = null;

    [Description( "Collection name to be updated. This parameter is used for development only." )]
    [CommandOption( "--collection" )]
    public string? Collection { get; init; } = null;

    [Description(
        "Makes a single page/document to be crawled intead of all pages/documents. Useful with --dry. When used, the <url> argument represents the single page/document to be crawled." )]
    [CommandOption( "--article" )]
    public string? SingleArticleUrl { get; init; }

    [Description( "Does not change any data and writes the retrieved snippets to console. Use with --verbose." )]
    [CommandOption( "--dry" )]
    public bool Dry { get; init; } = false;

    [Description(
        "Performs an incremental update of the live collection instead of a full rebuild: only changed/new source items are "
        + "re-indexed and removed items are deleted, updating the collection in place (no blue/green swap). "
        + "Falls back to a full build when no live collection exists yet." )]
    [CommandOption( "--incremental" )]
    public bool Incremental { get; init; } = false;
}