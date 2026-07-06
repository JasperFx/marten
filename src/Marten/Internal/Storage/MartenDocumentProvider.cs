#nullable enable
using System;
using Marten.Schema.BulkLoading;

namespace Marten.Internal.Storage;

/// <summary>
///     Marten's <see cref="DocumentProvider{T}"/> (#4821: the base moved to Weasel.Storage) —
///     carries the Postgres-specific services that are not part of the dialect-neutral provider
///     contract, currently the COPY-protocol <see cref="IBulkLoader{T}"/>.
/// </summary>
public class MartenDocumentProvider<T>: DocumentProvider<T> where T : notnull
{
    public MartenDocumentProvider(IBulkLoader<T> bulkLoader, IDocumentStorage<T> queryOnly,
        IDocumentStorage<T> lightweight, IDocumentStorage<T> identityMap, IDocumentStorage<T> dirtyTracking)
        : base(queryOnly, lightweight, identityMap, dirtyTracking)
    {
        BulkLoader = bulkLoader;
    }

    public IBulkLoader<T> BulkLoader { get; }
}

public static class DocumentProviderSelectExtensions
{
    /// <summary>
    ///     Pick the storage variant for a Marten session tracking mode. (Lived on
    ///     DocumentProvider itself before #4821; DocumentTracking is Marten public API and stays
    ///     off the moved neutral provider.)
    /// </summary>
    public static IDocumentStorage<T> Select<T>(this DocumentProvider<T> provider, DocumentTracking tracking)
        where T : notnull
    {
        return tracking switch
        {
            DocumentTracking.None => provider.Lightweight,
            DocumentTracking.QueryOnly => provider.QueryOnly,
            DocumentTracking.DirtyTracking => provider.DirtyTracking,
            DocumentTracking.IdentityOnly => provider.IdentityMap,
            _ => throw new ArgumentOutOfRangeException(nameof(tracking)),
        };
    }
}
