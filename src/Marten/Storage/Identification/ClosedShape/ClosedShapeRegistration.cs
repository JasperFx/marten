#nullable enable
using System;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Schema;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike: hook for registering a hand-written closed-shape
/// <see cref="DocumentStorage{T, TId}"/> with a live <see cref="DocumentStore"/>
/// instance. Bypasses <c>ProviderGraph</c>'s codegen-emit path for the
/// registered document type.
/// </summary>
/// <remarks>
/// Must be called <strong>before</strong> any session that touches
/// <typeparamref name="TDoc"/> is opened — <c>ProviderGraph.StorageFor&lt;T&gt;</c>
/// memoizes the provider on first access, and we need our pre-built one
/// to land first.
/// </remarks>
public static class ClosedShapeRegistration
{
    /// <summary>
    /// Register the closed-shape document storage for a <typeparamref name="TDoc"/>
    /// with a <see cref="Guid"/> id (uses sequential-GUID identity strategy
    /// — equivalent to Marten's default <c>SequentialGuidIdGeneration</c>).
    /// </summary>
    public static void UseLightweightSequentialGuidClosedShape<TDoc>(this IDocumentStore store)
        where TDoc : class
    {
        var documentStore = (DocumentStore)store;
        var options = documentStore.Options;
        var mapping = options.Storage.FindMapping(typeof(TDoc)).As<DocumentMapping>();

        if (mapping.IdType != typeof(Guid))
        {
            throw new InvalidOperationException(
                $"{typeof(TDoc).Name} must use a Guid id to register with sequential-GUID identity — found {mapping.IdType.Name}.");
        }

        var identification = new SequentialGuidIdentification<TDoc>(mapping.IdMember);
        RegisterClosedShape<TDoc, Guid>(documentStore, mapping, identification);
    }

    /// <summary>
    /// Register the closed-shape document storage for a <typeparamref name="TDoc"/>
    /// with a <see cref="string"/> id, using externally-assigned keys
    /// (callers set the id themselves; the strategy refuses to auto-generate).
    /// </summary>
    public static void UseExternallyAssignedStringClosedShape<TDoc>(this IDocumentStore store)
        where TDoc : class
    {
        var documentStore = (DocumentStore)store;
        var options = documentStore.Options;
        var mapping = options.Storage.FindMapping(typeof(TDoc)).As<DocumentMapping>();

        if (mapping.IdType != typeof(string))
        {
            throw new InvalidOperationException(
                $"{typeof(TDoc).Name} must use a string id for externally-assigned identity — found {mapping.IdType.Name}.");
        }

        var identification = new StringIdentification<TDoc>(mapping.IdMember);
        RegisterClosedShape<TDoc, string>(documentStore, mapping, identification);
    }

    /// <summary>
    /// Shared registration plumbing: builds the descriptor + 4 storage
    /// instances (one per StorageStyle) and appends them as a
    /// <see cref="DocumentProvider{T}"/> to the live ProviderGraph.
    /// </summary>
    private static void RegisterClosedShape<TDoc, TId>(
        DocumentStore documentStore,
        DocumentMapping mapping,
        IIdentification<TDoc, TId> identification)
        where TDoc : class
        where TId : notnull
    {
        var descriptor = DocumentStorageDescriptorBuilder.Build<TDoc, TId>(mapping, identification);

        var queryOnly = new QueryOnlyClosedShapeStorage<TDoc, TId>(mapping, descriptor);
        var lightweight = new LightweightClosedShapeStorage<TDoc, TId>(mapping, descriptor);
        var identityMap = new IdentityMapClosedShapeStorage<TDoc, TId>(mapping, descriptor);
        var dirtyTracking = new DirtyCheckedClosedShapeStorage<TDoc, TId>(mapping, descriptor);

        var bulkLoader = new SpikeNotImplementedBulkLoader<TDoc>();
        var provider = new DocumentProvider<TDoc>(
            bulkLoader,
            queryOnly: queryOnly,
            lightweight: lightweight,
            identityMap: identityMap,
            dirtyTracking: dirtyTracking);

        ((ProviderGraph)documentStore.Options.Providers).Append(provider);
    }
}
