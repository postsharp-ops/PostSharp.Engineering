// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Search.Backends;
using PostSharp.Engineering.BuildTools.Search.Backends.Typesense;
using PostSharp.Engineering.BuildTools.Search.Crawlers;
using PostSharp.Engineering.BuildTools.Search.Indexers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Typesense;

namespace PostSharp.Engineering.BuildTools.Search.Updaters;

internal class DocumentationUpdater : CollectionUpdater
{
    private readonly string _source;
    private readonly string _sourceUrl;
    private readonly ImmutableArray<string> _products;
    private readonly DocumentParserFactory _documentParserFactory;

    public DocumentationUpdater(
        string source,
        string sourceUrl,
        ImmutableArray<string> products,
        DocumentParserFactory documentParserFactory,
        SearchBackendBase backend ) : base( backend )
    {
        this._source = source;
        this._sourceUrl = sourceUrl;
        this._products = products;
        this._documentParserFactory = documentParserFactory;
    }

    public override async Task<bool> UpdateAsync( BuildContext context, UpdateSearchCommandSettings settings, string targetCollection )
    {
        var handler = new HttpClientHandler();
        handler.ClientCertificateOptions = ClientCertificateOption.Manual;

        handler.ServerCertificateCustomValidationCallback =
            ( _, _, _, _ ) => true;

        var web = new HttpClient( handler );

        using ( web )
        {
            var siteIndexer = new SiteIndexer( this.Backend, this._documentParserFactory, web, context.Console );

            if ( !string.IsNullOrEmpty( settings.SingleArticleUrl ) )
            {
                context.Console.WriteMessage( $"Indexing single page '{settings.SingleArticleUrl}' to '{targetCollection}' collection." );

                return await siteIndexer.IndexArticlesAsync( targetCollection, this._source, this._products, [settings.SingleArticleUrl], settings.Incremental );
            }
            else if ( settings.Incremental )
            {
                return await this.UpdateIncrementalAsync( context, settings, targetCollection, web, siteIndexer );
            }
            else
            {
                context.Console.WriteMessage( $"Indexing sitemap '{this._sourceUrl}' to '{targetCollection}' collection." );

                return await siteIndexer.IndexSiteMapAsync( targetCollection, this._source, this._products, this._sourceUrl );
            }
        }
    }

    /// <summary>
    /// Re-indexes only the pages whose sitemap <c>lastmod</c> is newer than what is stored (or which are new), deletes the
    /// pages that disappeared from the sitemap, and updates the target collection in place. When the collection is empty
    /// (e.g. a fresh full build via the incremental path), every page is treated as new.
    /// </summary>
    private async Task<bool> UpdateIncrementalAsync(
        BuildContext context,
        UpdateSearchCommandSettings settings,
        string targetCollection,
        HttpClient web,
        SiteIndexer siteIndexer )
    {
        context.Console.WriteMessage( $"Incrementally updating '{targetCollection}' from sitemap '{this._sourceUrl}'." );

        var entries = await new SiteMapReader( web ).GetEntriesAsync( this._sourceUrl );

        var sitemapByUrl = entries
            .GroupBy( e => e.Url )
            .ToDictionary( g => g.Key, g => g.Max( e => e.LastModified ) );

        var existing = await this.Backend.ExportDocumentsAsync<Snippet>( targetCollection );

        var storedByUrl = existing
            .Where( s => !string.IsNullOrEmpty( s.Url ) )
            .GroupBy( s => s.Url )
            .ToDictionary( g => g.Key, g => g.Max( s => s.LastModified ) );

        // A page is (re)crawled when it is new, or when the sitemap reports a newer lastmod than what we stored.
        // Pages without a lastmod in the sitemap (0) are only crawled when new; the periodic full rebuild reconciles the rest.
        var changed = sitemapByUrl
            .Where( kv => !storedByUrl.TryGetValue( kv.Key, out var stored ) || ( kv.Value > 0 && kv.Value > stored ) )
            .Select( kv => kv.Key )
            .ToList();

        var removed = storedByUrl.Keys
            .Where( u => !sitemapByUrl.ContainsKey( u ) )
            .ToList();

        context.Console.WriteMessage(
            $"Incremental update: {changed.Count} changed/new page(s), {removed.Count} removed page(s), out of {sitemapByUrl.Count} sitemap URL(s)." );

        foreach ( var url in changed.Concat( removed ) )
        {
            var deleted = await this.Backend.DeleteDocumentsAsync( targetCollection, $"url:=`{url}`" );

            if ( deleted > 0 )
            {
                context.Console.WriteMessage( $"Deleted {deleted} stale snippet(s) for '{url}'." );
            }
        }

        if ( changed.Count == 0 )
        {
            context.Console.WriteMessage( "No changed or new pages to crawl." );

            return true;
        }

        var lastModifiedByUrl = changed.ToDictionary( u => u, u => sitemapByUrl[u] );

        return await siteIndexer.IndexArticlesAsync(
            targetCollection,
            this._source,
            this._products,
            changed,
            upsert: true,
            lastModifiedByUrl: lastModifiedByUrl );
    }

    public override Schema CreateSchema( string collectionName ) => CollectionSchemaFactory.CreateSchema<Snippet>( collectionName );
}