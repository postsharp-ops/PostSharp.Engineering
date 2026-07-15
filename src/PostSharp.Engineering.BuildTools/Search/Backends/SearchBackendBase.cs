// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using JetBrains.Annotations;
using System.Collections.Generic;
using System.Threading.Tasks;
using Typesense;

namespace PostSharp.Engineering.BuildTools.Search.Backends;

[PublicAPI]
public abstract class SearchBackendBase
{
    public abstract Task CreateCollectionAsync( Schema schema );

    public abstract Task DeleteCollectionAsync( string collection );

    public abstract Task<bool> TryDeleteCollectionAsync( string collection );

    public abstract Task<IEnumerable<CollectionResponse>> RetrieveCollectionsAsync();

    public abstract Task UpsertCollectionAliasAsync( string alias, string targetCollection );

    public abstract Task DeleteCollectionAliasAsync( string alias );

    public abstract Task<string> GetTargetOfCollectionAliasAsync( string alias );

    public abstract Task<IEnumerable<CollectionAliasResponse>> RetrieveCollectionAliasesAsync();

    public abstract Task CreateDocumentsAsync<T>( string collection, IReadOnlyCollection<T> batch );

    public abstract Task UpsertDocumentsAsync<T>( string collection, IReadOnlyCollection<T> batch );

    public abstract Task UpdateDocumentsAsync<T>( string collection, IReadOnlyCollection<T> batch );

    public abstract Task EmplaceDocumentsAsync<T>( string collection, IReadOnlyCollection<T> batch );

    /// <summary>
    /// Deletes all documents matching a Typesense <c>filter_by</c> expression. Returns the number of deleted documents.
    /// Used by incremental indexing to remove stale documents.
    /// </summary>
    public abstract Task<int> DeleteDocumentsAsync( string collection, string filterBy );

    /// <summary>
    /// Exports all documents of a collection. Used by incremental indexing to read the currently-indexed state.
    /// </summary>
    public abstract Task<IReadOnlyList<T>> ExportDocumentsAsync<T>( string collection );

    /// <summary>
    /// Runs a search and returns the matching documents (without search metadata). Used by incremental indexing,
    /// e.g. to read the most recently updated document.
    /// </summary>
    public abstract Task<IReadOnlyList<T>> SearchDocumentsAsync<T>( string collection, SearchParameters searchParameters );
}