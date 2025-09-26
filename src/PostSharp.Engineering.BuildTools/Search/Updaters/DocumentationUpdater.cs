// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Build;
using PostSharp.Engineering.BuildTools.Search.Backends;
using PostSharp.Engineering.BuildTools.Search.Backends.Typesense;
using PostSharp.Engineering.BuildTools.Search.Crawlers;
using PostSharp.Engineering.BuildTools.Search.Indexers;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Typesense;

namespace PostSharp.Engineering.BuildTools.Search.Updaters;

internal class DocumentationUpdater : CollectionUpdater
{
    private readonly ImmutableArray<string> _products;
    private readonly DocumentParserFactory _documentParserFactory;

    public DocumentationUpdater( ImmutableArray<string> products, DocumentParserFactory documentParserFactory, SearchBackendBase backend ) : base( backend )
    {
        this._products = products;
        this._documentParserFactory = documentParserFactory;
    }

    public override async Task<bool> UpdateAsync( BuildContext context, UpdateSearchCommandSettings settings, string targetCollection )
    {
        var productExtension = context.Product.Extensions.OfType<UpdateSearchProductExtension>().Single();

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
                context.Console.WriteMessage( $"Indexing single page '{productExtension.SourceUrl}' to '{targetCollection}' collection." );

                return await siteIndexer.IndexArticlesAsync( targetCollection, productExtension.Source, this._products, [settings.SingleArticleUrl] );
            }
            else
            {
                context.Console.WriteMessage( $"Indexing sitemap '{productExtension.SourceUrl}' to '{targetCollection}' collection." );

                return await siteIndexer.IndexSiteMapAsync( targetCollection, productExtension.Source, this._products, productExtension.SourceUrl );
            }
        }
    }

    public override Schema CreateSchema( string collectionName ) => CollectionSchemaFactory.CreateSchema<Snippet>( collectionName );
}