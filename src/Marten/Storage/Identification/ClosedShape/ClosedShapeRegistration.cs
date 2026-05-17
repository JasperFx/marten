#nullable enable
using System;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Schema;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike: convenience hook for registering a hand-written closed-shape
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
    public static void UseLightweightSequentialGuidClosedShape<TDoc>(this IDocumentStore store)
        where TDoc : class
    {
        var documentStore = (DocumentStore)store;
        var options = documentStore.Options;

        var mapping = options.Storage.FindMapping(typeof(TDoc)).As<DocumentMapping>();

        if (mapping.IdType != typeof(Guid))
        {
            throw new InvalidOperationException(
                $"{typeof(TDoc).Name} must use a Guid id for the closed-shape spike — found {mapping.IdType.Name}.");
        }

        var identification = new SequentialGuidIdentification<TDoc>(mapping.IdMember);
        var storage = new LightweightSequentialGuidStorage<TDoc>(mapping, identification);

        // All four tracking modes resolve to the same closed-shape storage
        // for the spike — IdentityMap / DirtyTracking layering isn't part
        // of the proof. The QueryOnly slot is the same instance too; in
        // production W3 those become separate hand-written subclasses.
        var bulkLoader = new SpikeNotImplementedBulkLoader<TDoc>();
        var provider = new DocumentProvider<TDoc>(
            bulkLoader,
            queryOnly: storage,
            lightweight: storage,
            identityMap: storage,
            dirtyTracking: storage);

        ((ProviderGraph)options.Providers).Append(provider);
    }
}
