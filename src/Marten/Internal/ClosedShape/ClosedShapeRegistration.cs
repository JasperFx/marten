#nullable enable
using System;
using System.Linq;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;

namespace Marten.Internal.ClosedShape;

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
    /// Build a closed-shape <see cref="DocumentProvider{TDoc}"/> for any
    /// supported document mapping — the only document-storage path now
    /// that the Roslyn-emit codegen has been retired (#4404).
    /// </summary>
    internal static DocumentProvider<TDoc> BuildSupportedProvider<TDoc>(DocumentMapping mapping)
        where TDoc : notnull
    {
        if (mapping.IdStrategy is ValueTypeIdGeneration vt)
        {
            return BuildValueTypeProvider<TDoc>(mapping, vt);
        }

        if (mapping.IdType == typeof(Guid))
        {
            IIdentification<TDoc, Guid> identification = mapping.IdStrategy is GuidIdGeneration
                ? new GuidIdentification<TDoc>(mapping.IdMember)
                : new SequentialGuidIdentification<TDoc>(mapping.IdMember);
            return BuildProvider<TDoc, Guid>(mapping, identification);
        }

        if (mapping.IdType == typeof(string))
        {
            IIdentification<TDoc, string> identification = mapping.IdStrategy is IdentityKeyGeneration
                ? new IdentityKeyIdentification<TDoc>(mapping.IdMember, mapping.Alias, mapping.DocumentType)
                : new StringIdentification<TDoc>(mapping.IdMember);
            return BuildProvider<TDoc, string>(mapping, identification);
        }

        if (mapping.IdType == typeof(int))
        {
            var identification = new HiloIntIdentification<TDoc>(mapping.IdMember, mapping.DocumentType);
            return BuildProvider<TDoc, int>(mapping, identification);
        }

        if (mapping.IdType == typeof(long))
        {
            var identification = new HiloLongIdentification<TDoc>(mapping.IdMember, mapping.DocumentType);
            return BuildProvider<TDoc, long>(mapping, identification);
        }

        // Use InvalidDocumentException so the live-aggregation path in
        // QuerySession.TryGetStorageForLiveAggregation can catch it and
        // register the type as identity-less (the codegen path threw
        // the same kind for aggregates with no Id member).
        throw new Marten.Exceptions.InvalidDocumentException(
            $"No closed-shape id strategy is registered for {typeof(TDoc).FullName} (id type {mapping.IdType?.FullName}, strategy {mapping.IdStrategy?.GetType().FullName}).");
    }

    /// <summary>
    /// W3 spike (M15): build a closed-shape provider for a strong-typed
    /// id whose wrapper type isn't known at compile time. Constructs the
    /// per-wrapper <see cref="ValueTypeIdentification{TDoc, TWrapper, TInner}"/>
    /// via <c>CloseAndBuildAs</c> and hands the closed-shape descriptor
    /// the wrapper type as TId.
    /// </summary>
    private static DocumentProvider<TDoc> BuildValueTypeProvider<TDoc>(DocumentMapping mapping, ValueTypeIdGeneration vt)
        where TDoc : notnull
    {
        var wrapperType = vt.OuterType;
        var innerType = vt.SimpleType;

        var identification = typeof(ValueTypeIdentification<,,>)
            .CloseAndBuildAs<object>(
                mapping.IdMember, vt, (object)mapping.DocumentType,
                typeof(TDoc), wrapperType, innerType);

        var buildProvider = typeof(ClosedShapeRegistration)
            .GetMethod(nameof(BuildProvider), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(typeof(TDoc), wrapperType);

        return (DocumentProvider<TDoc>)buildProvider.Invoke(null, new object?[] { mapping, identification })!;
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

        // M16: real bulk loader, COPY-based, built from the descriptor's
        // column list. Lightweight is fine to use as the storage backing
        // here since BulkLoader<T, TId> only calls IDocumentStorage's
        // AssignIdentity / Identity through it.
        var bulkLoader = new ClosedShapeBulkLoader<TDoc, TId>(lightweight, descriptor, mapping);
        return new DocumentProvider<TDoc>(
            bulkLoader,
            queryOnly: queryOnly,
            lightweight: lightweight,
            identityMap: identityMap,
            dirtyTracking: dirtyTracking);
    }
}
