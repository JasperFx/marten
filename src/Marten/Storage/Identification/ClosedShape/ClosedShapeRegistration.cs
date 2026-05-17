#nullable enable
using System;
using System.Linq;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Schema.Identity;

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
        var provider = BuildProvider<TDoc, TId>(mapping, identification);
        ((ProviderGraph)documentStore.Options.Providers).Append(provider);
    }

    /// <summary>
    /// W3 spike (M6): is this mapping inside the closed-shape coverage
    /// envelope today? Used by <see cref="ProviderGraph"/> when
    /// <see cref="StoreOptions.UseClosedShapeDocumentStorage"/> is on to
    /// route eligible types through the closed-shape path and let the
    /// rest fall back to codegen.
    /// </summary>
    /// <remarks>
    /// Coverage as of this commit: Guid id with sequential-GUID generation
    /// or string id with externally-assigned keys; no optimistic
    /// concurrency, no numeric revisions, no soft delete, no
    /// hierarchical sub-classing, no duplicated fields. Tenancy
    /// (Single + Conjoined) is supported. Anything outside this envelope
    /// returns false.
    /// </remarks>
    public static bool IsSupported(DocumentMapping mapping)
    {
        if (mapping.UseNumericRevisions) return false;
        if (mapping.DeleteStyle == DeleteStyle.SoftDelete) return false;
        if (mapping.SubClasses.Any()) return false;
        if (mapping.DuplicatedFields.Count > 0) return false;
        if (mapping.IsHierarchy()) return false;

        if (mapping.IdType == typeof(Guid) && mapping.IdStrategy is SequentialGuidIdGeneration)
        {
            return true;
        }

        if (mapping.IdType == typeof(string) &&
            (mapping.IdStrategy is StringIdGeneration || mapping.IdStrategy is NoOpIdGeneration))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// W3 spike (M6): build a closed-shape <see cref="DocumentProvider{TDoc}"/>
    /// for a mapping that has already passed <see cref="IsSupported"/>.
    /// Called by <see cref="ProviderGraph"/> on the default path when the
    /// store-level flag is on.
    /// </summary>
    internal static DocumentProvider<TDoc> BuildSupportedProvider<TDoc>(DocumentMapping mapping)
        where TDoc : notnull
    {
        if (mapping.IdType == typeof(Guid))
        {
            var identification = new SequentialGuidIdentification<TDoc>(mapping.IdMember);
            return BuildProvider<TDoc, Guid>(mapping, identification);
        }

        if (mapping.IdType == typeof(string))
        {
            var identification = new StringIdentification<TDoc>(mapping.IdMember);
            return BuildProvider<TDoc, string>(mapping, identification);
        }

        throw new InvalidOperationException(
            $"Mapping for {typeof(TDoc).FullName} is outside the closed-shape coverage envelope — call IsSupported first.");
    }

    private static DocumentProvider<TDoc> BuildProvider<TDoc, TId>(
        DocumentMapping mapping,
        IIdentification<TDoc, TId> identification)
        where TDoc : notnull
        where TId : notnull
    {
        var descriptor = DocumentStorageDescriptorBuilder.Build<TDoc, TId>(mapping, identification);

        var queryOnly = new QueryOnlyClosedShapeStorage<TDoc, TId>(mapping, descriptor);
        var lightweight = new LightweightClosedShapeStorage<TDoc, TId>(mapping, descriptor);
        var identityMap = new IdentityMapClosedShapeStorage<TDoc, TId>(mapping, descriptor);
        var dirtyTracking = new DirtyCheckedClosedShapeStorage<TDoc, TId>(mapping, descriptor);

        var bulkLoader = new SpikeNotImplementedBulkLoader<TDoc>();
        return new DocumentProvider<TDoc>(
            bulkLoader,
            queryOnly: queryOnly,
            lightweight: lightweight,
            identityMap: identityMap,
            dirtyTracking: dirtyTracking);
    }
}
