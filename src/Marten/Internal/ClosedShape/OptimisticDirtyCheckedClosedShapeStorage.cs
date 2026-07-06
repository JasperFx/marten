#nullable enable
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.Selectors;
using Marten.Schema;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Optimistic</c> closed-shape DirtyChecked storage. #4659 leaf.
/// </summary>
internal sealed class OptimisticDirtyCheckedClosedShapeStorage<TDoc, TId>: DirtyCheckedClosedShapeStorage<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    public OptimisticDirtyCheckedClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(mapping, descriptor)
    {
    }

    public override Weasel.Storage.IStorageOperation Insert(TDoc document, IStorageSession session, string tenant)
        => new OptimisticClosedShapeInsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.ForType<TDoc, TId>());

    public override Weasel.Storage.IStorageOperation Update(TDoc document, IStorageSession session, string tenant)
        => new OptimisticClosedShapeUpdateOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.ForType<TDoc, TId>());

    public override Weasel.Storage.IStorageOperation Upsert(TDoc document, IStorageSession session, string tenant)
        => new OptimisticClosedShapeUpsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, OperationRole.Upsert, session.Versions.ForType<TDoc, TId>());

    public override Weasel.Storage.IStorageOperation Overwrite(TDoc document, IStorageSession session, string tenant)
        => new OptimisticClosedShapeOverwriteOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.ForType<TDoc, TId>());

    public override Weasel.Storage.IStorageOperation OverwriteProjected(TDoc document, string tenant)
        => new OptimisticClosedShapeOverwriteOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, null);

    // #4667 — null version tracker; see Lightweight peer for semantics.
    public override Weasel.Storage.IStorageOperation UpsertProjected(TDoc document, string tenant)
        => new OptimisticClosedShapeUpsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, OperationRole.Upsert, null);

    public override Weasel.Storage.IStorageOperation InsertProjected(TDoc document, string tenant)
        => new OptimisticClosedShapeInsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, null);

    public override Weasel.Storage.IStorageOperation UpdateProjected(TDoc document, string tenant)
        => new OptimisticClosedShapeUpdateOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, null);

    public override ISelector BuildSelector(IStorageSession session)
        => _descriptor.ResolveDocumentType is not null
            ? new HierarchicalOptimisticClosedShapeDirtyTrackingSelector<TDoc, TId>(session, _descriptor)
            : new FlatOptimisticClosedShapeDirtyTrackingSelector<TDoc, TId>(session, _descriptor);
}
