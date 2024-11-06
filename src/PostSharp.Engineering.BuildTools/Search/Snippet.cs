// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using PostSharp.Engineering.BuildTools.Search.Backends.Typesense;
using System.Text.Json.Serialization;

namespace PostSharp.Engineering.BuildTools.Search;

[PublicAPI]
public class Snippet
{
    [JsonPropertyName( "title" )]
    public string Title { get; set; } = null!;
    
    [JsonPropertyName( "breadcrumb" )]
    public string Breadcrumb { get; set; } = null!;
    
    [JsonPropertyName( "summary" )]
    public string Summary { get; set; } = null!;

    [JsonPropertyName( "h1" )]
    public string[] H1 { get; set; } = null!;
    
    [JsonPropertyName( "h2" )]
    public string[] H2 { get; set; } = null!;
    
    [JsonPropertyName( "h3" )]
    public string[] H3 { get; set; } = null!;
    
    [JsonPropertyName( "h4" )]
    public string[] H4 { get; set; } = null!;
    
    [JsonPropertyName( "h5" )]
    public string[] H5 { get; set; } = null!;
    
    [JsonPropertyName( "h6" )]
    public string[] H6 { get; set; } = null!;

    [JsonPropertyName( "text" )]
    public string[] Text { get; set; } = null!;

    [JsonPropertyName( "source" )]
    [Facet]
    public string Source { get; set; } = null!;

    [JsonPropertyName( "link" )]
    public string Link { get; set; } = null!;

    [JsonPropertyName( "products" )]
    [Facet]
    public string[] Products { get; set; } = null!;

    [JsonPropertyName( "kinds" )]
    [Facet]
    public string[] Kinds { get; set; } = null!;

    [JsonPropertyName( "kind-rank" )]
    public int KindRank { get; set; }

    [JsonPropertyName( "categories" )]
    [Facet]
    public string[] Categories { get; set; } = null!;

    [JsonPropertyName( "complexity-levels" )]
    [Facet]
    public int[] ComplexityLevels { get; set; } = null!;

    [JsonPropertyName( "complexity-level-rank" )]
    public int ComplexityLevelRank { get; set; }
    
    [JsonPropertyName( "navigation-level" )]
    [Facet]
    public int NavigationLevel { get; set; }
}