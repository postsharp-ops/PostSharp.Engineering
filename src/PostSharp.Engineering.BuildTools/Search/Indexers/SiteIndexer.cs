// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using HtmlAgilityPack;
using PostSharp.Engineering.BuildTools.Search.Backends;
using PostSharp.Engineering.BuildTools.Search.Crawlers;
using PostSharp.Engineering.BuildTools.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace PostSharp.Engineering.BuildTools.Search.Indexers;

public class SiteIndexer
{
    private readonly SearchBackendBase _search;
    private readonly DocumentParserFactory _parserFactory;
    private readonly HttpClient _web;
    private readonly ConsoleHelper _console;

    public SiteIndexer( SearchBackendBase search, DocumentParserFactory parserFactory, HttpClient web, ConsoleHelper console )
    {
        this._search = search;
        this._parserFactory = parserFactory;
        this._web = web;
        this._console = console;
    }

    public async Task<bool> IndexSiteMapAsync( string collection, string source, ImmutableArray<string> products, string url, bool upsert = false )
    {
        this._console.WriteMessage( $"Loading sitemap from '{url}'." );

        var entries = await new SiteMapReader( this._web ).GetEntriesAsync( url );

        this._console.WriteMessage( "Sitemap loaded." );

        var urls = entries.Select( e => e.Url ).ToList();

        var lastModifiedByUrl = entries
            .GroupBy( e => e.Url )
            .ToDictionary( g => g.Key, g => g.Max( e => e.LastModified ) );

        return await this.IndexArticlesAsync( collection, source, products, urls, upsert, lastModifiedByUrl );
    }

    public async Task<bool> IndexArticlesAsync(
        string collection,
        string source,
        ImmutableArray<string> products,
        IReadOnlyCollection<string> urls,
        bool upsert = false,
        IReadOnlyDictionary<string, long>? lastModifiedByUrl = null )
    {
        var sw = new Stopwatch();
        sw.Start();

        void WriteMessage( string message )
        {
            this._console.WriteMessage( $"{sw.Elapsed}: {message}" );
        }

        WriteMessage( "Indexing started." );

        const int parallelism = 8;
        const int batchSize = 40;
        var uploadBatchTasks = new List<Task>( parallelism );
        var batch = new List<Snippet>( batchSize );
        var finishedBatchesCount = 0;
        var parsedDocuments = new HashSet<string>();
        var fetchErrors = 0;
        var uploadErrors = 0;

        void StartUploadingBatch()
        {
            var documentsInBatch = batch
                .Select( s => s.Link
                             .Contains( '#', StringComparison.Ordinal )
                             ? s.Link.Substring( 0, s.Link.IndexOf( '#', StringComparison.Ordinal ) )
                             : s.Link )
                .Distinct()
                .ToArray();

            parsedDocuments.UnionWith( documentsInBatch );
            var parsedDocumentsInBatch = string.Join( "; ", documentsInBatch );

            WriteMessage( $"Batch parsed. Starting indexing. (Partially) parsed documents: {parsedDocumentsInBatch}." );

            var task = upsert
                ? this._search.UpsertDocumentsAsync( collection, batch.ToImmutableArray() )
                : this._search.CreateDocumentsAsync( collection, batch.ToImmutableArray() );

            uploadBatchTasks.Add( task );
            batch.Clear();
        }

        async Task AwaitForAnyBatchUploadTaskCompleted()
        {
            var completedTask = await Task.WhenAny( uploadBatchTasks.ToArray() );

            try
            {
                await completedTask;
            }
            catch ( Exception e )
            {
                this._console.WriteError( e.ToString() );
                uploadErrors++;
            }

            uploadBatchTasks.Remove( completedTask );
            finishedBatchesCount++;

            this._console.WriteMessage(
                $"{sw.Elapsed}: Batch completed. Queued: {uploadBatchTasks.Count}; Finished: {finishedBatchesCount}; Parsed documents: {parsedDocuments.Count}/{urls.Count}" );
        }

        foreach ( var url in urls )
        {
            Stream stream;

            this._console.WriteMessage( $"Fetching '{url}'." );

            try
            {
                stream = await this._web.GetStreamAsync( url );
            }
            catch ( Exception e )
            {
                // An individual page being unreachable (e.g. a stale sitemap entry that now 404s) is tolerated up to a
                // threshold; it should not abort the whole index and prevent the alias swap.
                this._console.WriteWarning( $"Skipping '{url}': {e.Message}" );
                fetchErrors++;

                continue;
            }

            HtmlDocument document;

            await using ( stream )
            {
                document = new HtmlDocument();
                document.Load( stream );
            }

            var snippets = await this._parserFactory.CreateParser().GetSnippetsFromDocument( document, source, url, products );

            if ( snippets.Count == 0 )
            {
                this._console.WriteWarning( $"{url}: No snippets found." );
            }
            else
            {
                var lastModified = lastModifiedByUrl != null && lastModifiedByUrl.TryGetValue( url, out var lm ) ? lm : 0;

                foreach ( var snippet in snippets )
                {
                    // Stamp the page URL and last-modification time so incremental updates can diff and delete by page.
                    snippet.Url = url;
                    snippet.LastModified = lastModified;

                    if ( uploadBatchTasks.Count == parallelism )
                    {
                        await AwaitForAnyBatchUploadTaskCompleted();
                    }

                    if ( batch.Count == batchSize )
                    {
                        StartUploadingBatch();
                    }

                    batch.Add( snippet );
                }
            }
        }

        if ( batch.Count > 0 )
        {
            StartUploadingBatch();
        }

        while ( uploadBatchTasks.Count > 0 )
        {
            await AwaitForAnyBatchUploadTaskCompleted();
        }

        // Fail on any upload error, when nothing was indexed, or when too many pages were unreachable; otherwise a few
        // stale/unreachable sitemap entries are tolerated so the (mostly complete) index is still published.
        var maxToleratedFetchErrors = Math.Max( 5, urls.Count / 10 );

        if ( uploadErrors > 0 || parsedDocuments.Count == 0 || fetchErrors > maxToleratedFetchErrors )
        {
            this._console.WriteError(
                $"{sw.Elapsed}: Indexing failed. Parsed {parsedDocuments.Count}/{urls.Count} document(s); {fetchErrors} fetch error(s), {uploadErrors} upload error(s)." );

            return false;
        }
        else
        {
            if ( fetchErrors > 0 )
            {
                this._console.WriteWarning( $"{sw.Elapsed}: Indexing completed with {fetchErrors} unreachable page(s) skipped." );
            }
            else
            {
                this._console.WriteMessage( $"{sw.Elapsed}: Indexing completed." );
            }

            return true;
        }
    }
}