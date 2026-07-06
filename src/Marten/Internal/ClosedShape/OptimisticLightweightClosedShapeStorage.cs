#nullable enable
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.Selectors;
using Marten.Schema;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Optimistic</c> closed-shape Lightweight storage.
/// Factories construct <c>Optimistic…</c> operation leaves and pass the
/// session's typed Guid version tracker; BuildSelector returns the
/// optimistic read selector. #4659 leaf.
/// </summary>
internal sealed class OptimisticLightweightClosedShapeStorage<TDoc, TId>: LightweightClosedShapeStorage<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    public OptimisticLightweightClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(mapping, descriptor)
    {
    }

    public override IStorageOperation Insert(TDoc document, IStorageSession session, string tenant)
        => new OptimisticClosedShapeInsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.ForType<TDoc, TId>());

    public override IStorageOperation Update(TDoc document, IStorageSession session, string tenant)
        => new OptimisticClosedShapeUpdateOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.ForType<TDoc, TId>());

    public override IStorageOperation Upsert(TDoc document, IStorageSession session, string tenant)
        => new OptimisticClosedShapeUpsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, OperationRole.Upsert, session.Versions.ForType<TDoc, TId>());

    public override IStorageOperation Overwrite(TDoc document, IStorageSession session, string tenant)
        => new OptimisticClosedShapeOverwriteOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.ForType<TDoc, TId>());

    public override IStorageOperation OverwriteProjected(TDoc document, string tenant)
        // #4658: projection path passes null tracker so it doesn't poison
        // the session's optimistic-version map for the projected doc.
        => new OptimisticClosedShapeOverwriteOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, null);

    // #4667 — projection write paths pass null tracker (no session-shared
    // dict access). Optimistic Upsert/Update with null tracker bind DBNull
    // for the WHERE-guard; existing rows are left untouched on the ON CONFLICT
    // branch. Callers in the projection runtime set IgnoreConcurrencyViolation
    // = true (see ProjectionStorage.StoreProjection / Store flows) to suppress
    // the resulting "no row" exception.
    public override IStorageOperation UpsertProjected(TDoc document, string tenant)
        => new OptimisticClosedShapeUpsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, OperationRole.Upsert, null);

    public override IStorageOperation InsertProjected(TDoc document, string tenant)
        => new OptimisticClosedShapeInsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, null);

    public override IStorageOperation UpdateProjected(TDoc document, string tenant)
        => new OptimisticClosedShapeUpdateOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, null);

    public override ISelector BuildSelector(IStorageSession session)
        => _descriptor.ResolveDocumentType is not null
            ? new HierarchicalOptimisticClosedShapeLightweightSelector<TDoc, TId>(session, _descriptor)
            : new FlatOptimisticClosedShapeLightweightSelector<TDoc, TId>(session, _descriptor);
}
