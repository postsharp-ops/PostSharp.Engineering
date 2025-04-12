// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Search.Crawlers;

public class PlainDocumentParser : DocumentParser
{
    private readonly IReadOnlyList<string> _rootPaths;

    public PlainDocumentParser( params IEnumerable<string> rootPaths )
    {
        this._rootPaths = rootPaths.ToList();
    }

    public override Task<IReadOnlyCollection<Snippet>> GetSnippetsFromDocument(
        HtmlDocument document,
        string source,
        string url,
        ImmutableArray<string> products )
    {
        if ( document.DocumentNode == null )
        {
            throw new ArgumentException( nameof(document) );
        }
        
        var snippets = new List<Snippet>();

        var title = document.DocumentNode.SelectSingleNode( "/html/head/meta[@name=\"title\"]" )?.Attributes["content"]?.Value ??
                    document.DocumentNode.SelectSingleNode( "/html/head/title" )?.GetText()
                    ?? throw new InvalidOperationException( "Title is missing." );

        title = title.Trim()
            .Replace(
                "&#xD;&#xA;",
                "",
                StringComparison.OrdinalIgnoreCase ); // This is appended to each title by the HelpServer. Might be a bug.

        var summaryNode = document.DocumentNode.SelectSingleNode( "/html/head/meta[@name=\"description\"]" );

        var summary = summaryNode?.Attributes["content"]?.Value?.Trim() ?? "";

        var keywordsNode = document.DocumentNode?.SelectSingleNode( "/html/head/meta[@name=\"keywords\"]" );
        var keywords = keywordsNode?.Attributes["content"]?.Value?.Trim() ?? "";
        
        var breadcrumbLinks = document.DocumentNode
            .SelectSingleNode( "//nav[@itemtype=\"https://schema.org/BreadcrumbList\"]" )
            ?
            .SelectNodes( ".//span[@itemprop=\"name\"]" )
            .Select( node => node.GetText() )
            .ToArray();

        var breadcrumb = breadcrumbLinks != null ? string.Join( " > ", breadcrumbLinks ) : "";

        foreach ( var rootPath in this._rootPaths )
        {
            var roots = document.DocumentNode.SelectNodes( rootPath );

            if ( roots == null )
            {
                continue;
            }

            foreach ( var rootNode in roots )
            {
                var h1 = rootNode.SelectNodes( "//h1" )?.Select( x => x.GetText() ).ToArray() ?? [];
                var h2 = rootNode.SelectNodes( "//h2" )?.Select( x => x.GetText() ).ToArray() ?? [];
                var h3 = rootNode.SelectNodes( "//h3" )?.Select( x => x.GetText() ).ToArray() ?? [];
                var h4 = rootNode.SelectNodes( "//h4" )?.Select( x => x.GetText() ).ToArray() ?? [];
                var h5 = rootNode.SelectNodes( "//h5" )?.Select( x => x.GetText() ).ToArray() ?? [];
                var text = rootNode.GetText();

                snippets.Add(
                    new Snippet()
                    {
                        Title = title,
                        Summary = summary,
                        Keywords = keywords,
                        Text = [text],
                        H1 = h1,
                        H2 = h2,
                        H3 = h3,
                        H4 = h4,
                        H5 = h5,
                        Link = url,
                        Source = source,
                        Breadcrumb = breadcrumb
                    } );
            }
        }

        return Task.FromResult<IReadOnlyCollection<Snippet>>( snippets );
    }
}