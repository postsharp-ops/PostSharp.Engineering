// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Search.Backends.Typesense;
using System.Text.Json.Serialization;

namespace PostSharp.Engineering.BuildTools.Search;

[PublicAPI]
public class Snippet
{
    [JsonPropertyName( "title" )]
    public string Title { get; set; } = "";

    [JsonPropertyName( "breadcrumb" )]
    public string Breadcrumb { get; set; } = "";

    [JsonPropertyName( "summary" )]
    public string Summary { get; set; } = "";

    [JsonPropertyName( "h1" )]
    public string[] H1 { get; set; } = [];

    [JsonPropertyName( "h2" )]
    public string[] H2 { get; set; } = [];

    [JsonPropertyName( "h3" )]
    public string[] H3 { get; set; } = [];

    [JsonPropertyName( "h4" )]
    public string[] H4 { get; set; } = [];

    [JsonPropertyName( "h5" )]
    public string[] H5 { get; set; } = [];

    [JsonPropertyName( "h6" )]
    public string[] H6 { get; set; } = [];

    [JsonPropertyName( "text" )]
    public string[] Text { get; set; } = [];

    [JsonPropertyName( "keywords" )]
    public string Keywords { get; set; } = "";

    [JsonPropertyName( "source" )]
    [Facet]
    public string Source { get; set; } = "";

    [JsonPropertyName( "link" )]
    public string Link { get; set; } = "";

    /// <summary>
    /// Gets or sets the URL of the page this snippet belongs to (i.e. <see cref="Link"/> without the section anchor).
    /// Used as the delete-by-filter key during incremental indexing.
    /// </summary>
    [JsonPropertyName( "url" )]
    [Facet]
    public string Url { get; set; } = "";

    /// <summary>
    /// Gets or sets the last-modification time of the page (Unix time in seconds, from the sitemap <c>lastmod</c>),
    /// or 0 when unknown. Used by incremental indexing to detect changed pages.
    /// </summary>
    [JsonPropertyName( "lastmod" )]
    public long LastModified { get; set; }

    [JsonPropertyName( "products" )]
    [Facet]
    public string[] Products { get; set; } = [];

    [JsonPropertyName( "kinds" )]
    [Facet]
    public string[] Kinds { get; set; } = [];

    [JsonPropertyName( "kind-rank" )]
    public int KindRank { get; set; }

    [JsonPropertyName( "categories" )]
    [Facet]
    public string[] Categories { get; set; } = [];

    [JsonPropertyName( "complexity-levels" )]
    [Facet]
    public int[] ComplexityLevels { get; set; } = [];

    [JsonPropertyName( "complexity-level-rank" )]
    public int ComplexityLevelRank { get; set; }

    [JsonPropertyName( "navigation-level" )]
    [Facet]
    public int NavigationLevel { get; set; }
}