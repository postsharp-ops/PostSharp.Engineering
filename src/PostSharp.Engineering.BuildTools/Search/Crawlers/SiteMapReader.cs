// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Search.Crawlers;

/// <summary>
/// A single <c>&lt;url&gt;</c> entry of a sitemap.
/// </summary>
/// <param name="Url">The value of the <c>&lt;loc&gt;</c> element.</param>
/// <param name="LastModified">The <c>&lt;lastmod&gt;</c> element as Unix time in seconds, or 0 when absent/unparseable.</param>
internal record SiteMapEntry( string Url, long LastModified );

internal class SiteMapReader
{
    private readonly HttpClient _client;

    public SiteMapReader( HttpClient client )
    {
        this._client = client;
    }

    /// <summary>
    /// Returns the URLs of a sitemap. Kept for backward compatibility; see <see cref="GetEntriesAsync"/> for last-modification times.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetDocumentsAsync( string url )
        => (await this.GetEntriesAsync( url )).Select( e => e.Url ).ToList();

    /// <summary>
    /// Returns the entries of a sitemap, including the <c>lastmod</c> time when present.
    /// </summary>
    public async Task<IReadOnlyList<SiteMapEntry>> GetEntriesAsync( string url )
    {
        await using var stream = await this._client.GetStreamAsync( url );
        var sitemap = new HtmlDocument();
        sitemap.Load( stream );

        var urlNodes = sitemap.DocumentNode.Element( "urlset" )?.Elements( "url" );

        if ( urlNodes == null )
        {
            return [];
        }

        var entries = new List<SiteMapEntry>();

        foreach ( var node in urlNodes )
        {
            var loc = node.Element( "loc" )?.GetDirectInnerText();

            if ( string.IsNullOrWhiteSpace( loc ) )
            {
                continue;
            }

            var lastModText = node.Element( "lastmod" )?.GetDirectInnerText();

            entries.Add( new SiteMapEntry( loc.Trim(), ParseLastModified( lastModText ) ) );
        }

        return entries;
    }

    private static long ParseLastModified( string? text )
    {
        if ( !string.IsNullOrWhiteSpace( text )
             && DateTimeOffset.TryParse(
                 text,
                 CultureInfo.InvariantCulture,
                 DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                 out var value ) )
        {
            return value.ToUnixTimeSeconds();
        }

        return 0;
    }
}