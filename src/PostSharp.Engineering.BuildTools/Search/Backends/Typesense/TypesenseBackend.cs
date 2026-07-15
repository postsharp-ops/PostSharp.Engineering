// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Typesense;
using Typesense.Setup;

namespace PostSharp.Engineering.BuildTools.Search.Backends.Typesense;

public class TypesenseBackend : SearchBackendBase
{
    private readonly ITypesenseClient _client;

    public TypesenseBackend( string apiKey, string host, string port, string protocol = "http" )
    {
        var services = new ServiceCollection()
            .AddTypesenseClient( config =>
            {
                config.ApiKey = apiKey;
                config.Nodes = new List<Node> { new( host, port, protocol ) };
            } )
            .BuildServiceProvider();

        this._client = services.GetService<ITypesenseClient>()!;
    }

    public override Task CreateCollectionAsync( Schema schema ) => this._client.CreateCollection( schema );

    public override Task DeleteCollectionAsync( string collection ) => this._client.DeleteCollection( collection );

    public override async Task<bool> TryDeleteCollectionAsync( string collection )
    {
        if ( (await this._client.RetrieveCollections()).Any( r => r.Name == collection ) )
        {
            _ = await this._client.DeleteCollection( collection );

            return true;
        }
        else
        {
            return false;
        }
    }

    public override async Task<IEnumerable<CollectionResponse>> RetrieveCollectionsAsync() => await this._client.RetrieveCollections();

    public override Task UpsertCollectionAliasAsync( string alias, string targetCollection )
        => this._client.UpsertCollectionAlias( alias, new CollectionAlias( targetCollection ) );

    public override Task DeleteCollectionAliasAsync( string alias ) => this._client.DeleteCollectionAlias( alias );

    public override async Task<IEnumerable<CollectionAliasResponse>> RetrieveCollectionAliasesAsync()
        => (await this._client.ListCollectionAliases()).CollectionAliases;

    public override async Task<string> GetTargetOfCollectionAliasAsync( string alias )
    {
        var response = await this._client.RetrieveCollectionAlias( alias );

        return response.CollectionName;
    }

    private async Task CreateDocumentsAsync<T>( string collection, IReadOnlyCollection<T> batch, ImportType importType )
    {
        var responses = await this._client.ImportDocuments( collection, batch, batch.Count, importType );
        var failedImports = responses.Where( r => !r.Success ).ToArray();

        if ( failedImports.Length != 0 )
        {
            throw new TypesenseImportFailedException( failedImports );
        }
    }

    public override Task CreateDocumentsAsync<T>( string collection, IReadOnlyCollection<T> batch )
        => this.CreateDocumentsAsync( collection, batch, ImportType.Create );

    public override Task UpsertDocumentsAsync<T>( string collection, IReadOnlyCollection<T> batch )
        => this.CreateDocumentsAsync( collection, batch, ImportType.Upsert );

    public override Task UpdateDocumentsAsync<T>( string collection, IReadOnlyCollection<T> batch )
        => this.CreateDocumentsAsync( collection, batch, ImportType.Update );

    public override Task EmplaceDocumentsAsync<T>( string collection, IReadOnlyCollection<T> batch )
        => this.CreateDocumentsAsync( collection, batch, ImportType.Emplace );

    public override async Task<int> DeleteDocumentsAsync( string collection, string filterBy )
    {
        var response = await this._client.DeleteDocuments( collection, filterBy, 100 );

        return response.NumberOfDeleted;
    }

    public override async Task<IReadOnlyList<T>> ExportDocumentsAsync<T>( string collection )
        => await this._client.ExportDocuments<T>( collection, CancellationToken.None );

    public override async Task<IReadOnlyList<T>> SearchDocumentsAsync<T>( string collection, SearchParameters searchParameters )
    {
        var result = await this._client.Search<T>( collection, searchParameters, CancellationToken.None );

        return result.Hits.Select( h => h.Document ).ToList();
    }
}