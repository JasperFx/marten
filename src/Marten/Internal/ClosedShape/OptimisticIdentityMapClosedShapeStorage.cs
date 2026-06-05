#nullable enable
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.Selectors;
using Marten.Schema;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Optimistic</c> closed-shape IdentityMap storage. #4659 leaf.
/// </summary>
internal sealed class OptimisticIdentityMapClosedShapeStorage<TDoc, TId>: IdentityMapClosedShapeStorage<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    public OptimisticIdentityMapClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(mapping, descriptor)
    {
    }

    public override IStorageOperation Insert(TDoc document, IMartenSession session, string tenant)
        => new OptimisticClosedShapeInsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.ForType<TDoc, TId>());

    public override IStorageOperation Update(TDoc document, IMartenSession session, string tenant)
        => new OptimisticClosedShapeUpdateOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.ForType<TDoc, TId>());

    public override IStorageOperation Upsert(TDoc document, IMartenSession session, string tenant)
        => new OptimisticClosedShapeUpsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, OperationRole.Upsert, session.Versions.ForType<TDoc, TId>());

    public override IStorageOperation Overwrite(TDoc document, IMartenSession session, string tenant)
        => new OptimisticClosedShapeOverwriteOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.ForType<TDoc, TId>());

    public override IStorageOperation OverwriteProjected(TDoc document, string tenant)
        => new OptimisticClosedShapeOverwriteOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, null);

    // #4667 — null version tracker; see Lightweight peer for semantics.
    public override IStorageOperation UpsertProjected(TDoc document, string tenant)
        => new OptimisticClosedShapeUpsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, OperationRole.Upsert, null);

    public override IStorageOperation InsertProjected(TDoc document, string tenant)
        => new OptimisticClosedShapeInsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, null);

    public override IStorageOperation UpdateProjected(TDoc document, string tenant)
        => new OptimisticClosedShapeUpdateOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, null);

    public override ISelector BuildSelector(IMartenSession session)
        => _descriptor.HierarchyMapping is not null
            ? new HierarchicalOptimisticClosedShapeIdentityMapSelector<TDoc, TId>(session, _descriptor)
            : new FlatOptimisticClosedShapeIdentityMapSelector<TDoc, TId>(session, _descriptor);
}
