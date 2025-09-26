// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using HtmlAgilityPack;
using JetBrains.Annotations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Search.Crawlers;

[PublicAPI]
public abstract class DocFxDocumentParser : DocumentParser
{
    private static readonly char _newLineCharacter = Environment.NewLine[^1];
    private ContentInfo? _contentInfo;
    private string _titlePrefix = "";
    private string _title = "";
    private string? _canonicalUrl;
    private string? _anchor;
    private string _summary = "";
    private string _keywords = "";
    private readonly List<string> _h1 = [];
    private readonly List<string> _h2 = [];
    private readonly List<string> _h3 = [];
    private readonly List<string> _h4 = [];
    private readonly List<string> _h5 = [];
    private readonly List<string> _h6 = [];
    private readonly IReadOnlyDictionary<int, List<string>> _headings;
    private readonly StringBuilder _textBuilder = new();
    private readonly List<string> _text = [];
    private bool _isTextPreformatted;
    private bool _isParagraphIgnored;
    private bool _isCurrentContentIgnored;
    private readonly List<Snippet> _snippets = [];

    protected DocFxDocumentParser()
    {
        this._headings = new Dictionary<int, List<string>>
        {
            { 1, this._h1 },
            { 2, this._h2 },
            { 3, this._h3 },
            { 4, this._h4 },
            { 5, this._h5 },
            { 6, this._h6 }
        }.ToImmutableDictionary();
    }

    public override async Task<IReadOnlyCollection<Snippet>> GetSnippetsFromDocument(
        HtmlDocument document,
        string source,
        string url,
        ImmutableArray<string> products )
    {
        // TODO: The design could be improved, so we don't need to check this.
        if ( this._contentInfo != null )
        {
            throw new InvalidOperationException( "This object is not reusable." );
        }

        var breadcrumbLinks = document.DocumentNode
            .SelectSingleNode( "//nav[@itemtype=\"https://schema.org/BreadcrumbList\"]" )
            .SelectNodes( ".//span[@itemprop=\"name\"]" )
            .Select( node => node.GetText() )
            .ToArray();

        var breadcrumb = this.GetBreadcrumbData( breadcrumbLinks );

        if ( breadcrumb.IsPageIgnored )
        {
            return [];
        }

        var canonicalUrlNode = document.DocumentNode.SelectSingleNode( "//link[@rel=\"canonical\"]" );
        this._canonicalUrl = canonicalUrlNode?.Attributes["href"]?.Value?.Trim() ?? throw new InvalidOperationException( "Canonical URL is missing." );

        var titleNode = document.DocumentNode.SelectSingleNode( "//meta[@name=\"title\"]" );

        this._title = titleNode?
                          .Attributes["content"]
                          ?.Value
                          ?.Trim()
                          .Replace( "&#xD;&#xA;", "", StringComparison.OrdinalIgnoreCase ) // This is appended to each title by the HelpServer. Might be a bug.
                      ?? throw new InvalidOperationException( "Title is missing." );

        var summaryNode = document.DocumentNode.SelectSingleNode( "//meta[@name=\"description\"]" );

        this._summary = summaryNode?.Attributes["content"]?.Value?.Trim() ?? "";
        var keywordsNode = titleNode?.SelectSingleNode( "//meta[@name=\"keywords\"]" );

        this._keywords = keywordsNode?.Attributes["content"]?.Value?.Trim() ?? "";

        // API pages containing H4s that don't belong to member lists
        // are split to individual snippets according to the H4s.
        if ( breadcrumb.IsApiDoc && (document.DocumentNode.SelectNodes( "//h4" )
                ?.Any( h =>
                {
                    var id = h.Attributes["id"]?.Value;

                    return id == null || !DocFxApiArticleHelper.IsMemberListItemId( id );
                } ) ?? false) )
        {
            // Eg. Method, Constructor, Property, Operator, ...
            this._titlePrefix = this._title.Split(
                ' ',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries )[0] + " ";

            this._isParagraphIgnored = true;
            this._isCurrentContentIgnored = true;
        }

        var complexityLevelNode = document.DocumentNode.SelectSingleNode( "//meta[@name=\"postsharp:level\"]" );
        var complexityLevelString = complexityLevelNode?.Attributes["content"]?.Value;

        var complexityLevel = string.IsNullOrEmpty( complexityLevelString )
            ? 0
            : int.Parse( complexityLevelString, CultureInfo.InvariantCulture );

        var complexityLevelRank = 1000 - complexityLevel - 1;

        this._contentInfo = new ContentInfo(
            breadcrumb.Breadcrumb,
            breadcrumb.Kinds,
            breadcrumb.KindRank,
            breadcrumb.Categories,
            source,
            products.ToArray(),
            complexityLevel,
            complexityLevelRank,
            breadcrumb.NavigationLevel,
            breadcrumb.IsApiDoc );

        var snippets = await this.CrawlAsync(
            document.DocumentNode
                .SelectSingleNode( "//div[@id=\"content\"]" ) );

        return snippets;
    }

    protected abstract BreadcrumbInfo GetBreadcrumbData( string[] breadcrumbLinks );

    private async Task<IReadOnlyCollection<Snippet>> CrawlAsync( HtmlNode node )
    {
        await this.ParseRecursivelyAsync( node, true );
        this.CreateSnippet();

        return this._snippets.ToArray();
    }

    private async Task ParseRecursivelyAsync( HtmlNode node, bool skipNextSibling = false )
    {
        await Task.Yield();

        var skipChildNodes = false;
        var nodeName = node.Name.ToLowerInvariant();

        if ( Regex.IsMatch( nodeName, @"^h\d$" ) )
        {
            var level = node.Name[1] - '0';
            var text = this.GetNodeText( node );

            if ( this._contentInfo!.IsApiDoc )
            {
                var strategy = DocFxApiArticleHelper.GetNextParagraphStrategy( node );

                if ( strategy.IsNextSnippet )
                {
                    if ( !this._isCurrentContentIgnored )
                    {
                        this.CreateSnippet();
                    }

                    this._isCurrentContentIgnored = false;
                    this._title = text;

                    this._anchor = node.Attributes["id"]?.Value;
                }
                else
                {
                    this.FinishParagraph();
                }

                this._isParagraphIgnored = strategy.IsIgnored;
            }
            else
            {
                this.FinishParagraph();
                this._headings[level].Add( text );
            }

            skipChildNodes = true;
        }
        else if ( node.NodeType == HtmlNodeType.Text )
        {
            var innerText = this.GetNodeText( node );

            if ( innerText != "" )
            {
                if ( this._textBuilder.Length > 0 && char.IsLetterOrDigit( innerText[0] ) && !char.IsWhiteSpace( this._textBuilder[^1] ) )
                {
                    this._textBuilder.Append( ' ' );
                }

                this._textBuilder.Append( innerText );
            }
        }
        else
        {
            switch ( nodeName )
            {
                case "div":
                    {
                        var cssClass = node.Attributes["class"]?.Value ?? "";

                        // Ignore code links in examples (See on GitHub, open in sandbox, ...)
                        if ( cssClass.Contains( "project-buttons", StringComparison.OrdinalIgnoreCase )

                             // Ignore code links (Open in sandbox, See on GitHub, ...)
                             || cssClass.Contains( "sample-links", StringComparison.OrdinalIgnoreCase )

                             // Ignore tabbed code
                             || cssClass.Contains( "tabGroup", StringComparison.OrdinalIgnoreCase )

                             // Ignore diff code in examples
                             || cssClass.Contains( "compare", StringComparison.OrdinalIgnoreCase )

                             // Ignore graphs.
                             || cssClass.Contains( "mermaid", StringComparison.OrdinalIgnoreCase ) )
                        {
                            skipChildNodes = true;
                        }

                        break;
                    }

                // Ignore code
                case "code":
                    skipChildNodes = true;

                    break;

                case "li":
                    this._textBuilder.Append( "- " );

                    break;

                case "pre":
                    this._isTextPreformatted = true;

                    break;

                case "table":
                    this.CrawlTable( node );

                    skipChildNodes = true;

                    break;
            }
        }

        if ( !skipChildNodes && node.HasChildNodes )
        {
            await this.ParseRecursivelyAsync( node.FirstChild );
        }

        switch ( nodeName )
        {
            case "br":
                this._textBuilder.AppendLine();

                break;

            // TODO: We could improve this by figuring out if the node is a block element.
            case "p" or "div" or "li" or "pre" when this._textBuilder.Length > 0
                                                    && this._textBuilder[^1] != _newLineCharacter:
                this._textBuilder.AppendLine();
                this.FinishParagraph();

                break;

            case "pre":
                this._isTextPreformatted = false;

                break;
        }

        if ( !skipNextSibling && node.NextSibling != null )
        {
            await this.ParseRecursivelyAsync( node.NextSibling );
        }
    }

    private void FinishParagraph()
    {
        // TODO: The performance can be improved by skipping the ignored nodes,
        // but it's not worth the effort at the moment.
        if ( !this._isParagraphIgnored )
        {
            var text = this._textBuilder.ToString().Trim();

            if ( text.Length > 0 )
            {
                this._text.Add( text );
            }
        }

        this._textBuilder.Clear();
    }

    private void CreateSnippet()
    {
        if ( this._contentInfo == null )
        {
            throw new InvalidOperationException( $"{nameof(this._contentInfo)} field is not set." );
        }

        if ( this._canonicalUrl == null )
        {
            throw new InvalidOperationException( $"{nameof(this._canonicalUrl)} field is not set." );
        }

        var link = this._anchor == null
            ? this._canonicalUrl
            : $"{this._canonicalUrl}#{this._anchor}";

        string summary;

        if ( this._contentInfo.IsApiDoc )
        {
            summary = this._text.Count > 0 ? this._text[0] : this._summary;
        }
        else
        {
            summary = this._summary;
        }

        var snippet = new Snippet()
        {
            Breadcrumb = this._contentInfo.Breadcrumb,
            Title = $"{this._titlePrefix}{this._title}",
            Summary = summary,
            Text = this._text.ToArray(),
            Keywords = this._keywords,
            H1 = this._h1.ToArray(),
            H2 = this._h2.ToArray(),
            H3 = this._h3.ToArray(),
            H4 = this._h4.ToArray(),
            H5 = this._h5.ToArray(),
            H6 = this._h6.ToArray(),
            Link = link,
            Source = this._contentInfo.Source,
            Products = this._contentInfo.Products,
            Kinds = this._contentInfo.Kinds,
            KindRank = this._contentInfo.KindRank,
            Categories = this._contentInfo.Categories,
            ComplexityLevels = this._contentInfo.ComplexityLevel < 100 ? [] : [this._contentInfo.ComplexityLevel],
            ComplexityLevelRank = this._contentInfo.ComplexityLevelRank,
            NavigationLevel = this._contentInfo.NavigationLevel
        };

        this.FinishParagraph();

        this._h2.Clear();
        this._h3.Clear();
        this._h4.Clear();
        this._h5.Clear();
        this._h6.Clear();
        this._summary = "";
        this._text.Clear();

        this._snippets.Add( snippet );
    }

    private string GetNodeText( HtmlNode node ) => node.GetText( this._isTextPreformatted );

    private void CrawlTable( HtmlNode table )
    {
        // TODO: rowspan, colspan
        // TODO: Crawl children instead of using GetText.

        var headers = table.SelectNodes( ".//th" )?.Select( h => h.GetText() ).ToList() ?? [];

        foreach ( var row in table.SelectNodes( ".//tr" ) )
        {
            var rowText = string.Join(
                    Environment.NewLine,
                    row
                        ?.SelectNodes( "./td" )
                        ?.Select( ( c, i ) => $"{(i < headers.Count ? headers[i] : "_")}: {c.GetText()}" )
                    ?? [] )
                .Trim();

            this._textBuilder.AppendLine( rowText );
            this.FinishParagraph();
        }
    }

    // Make each first letter upper-case
    protected static string NormalizeCategoryName( string category )
    {
        if ( category.Length < 2 )
        {
            throw new InvalidOperationException( "Invalid category name." );
        }

        return string.Join( ' ', category.Split( ' ' ).Select( s => $"{char.ToUpperInvariant( s[0] )}{s.Substring( 1 )}" ) );
    }
}