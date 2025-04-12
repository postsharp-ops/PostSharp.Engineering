// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using HtmlAgilityPack;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Search.Crawlers;

internal class SiteMapReader
{
    private readonly HttpClient _client;

    public SiteMapReader( HttpClient client )
    {
        this._client = client;
    }

    public async Task<IReadOnlyList<string>> GetDocumentsAsync( string url )
    {
        await using var stream = await this._client.GetStreamAsync( url );
        var sitemap = new HtmlDocument();
        sitemap.Load( stream );

        var documents =
            sitemap.DocumentNode
                .Element( "urlset" )
                .Elements( "url" )
                .Select( e => e.Element( "loc" ).GetDirectInnerText() )
                .ToImmutableArray();

        return documents;
    }
}