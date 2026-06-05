#nullable enable
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.Selectors;
using Marten.Schema;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Off</c> closed-shape DirtyChecked storage. #4659 leaf.
/// </summary>
internal sealed class UnversionedDirtyCheckedClosedShapeStorage<TDoc, TId>: DirtyCheckedClosedShapeStorage<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    public UnversionedDirtyCheckedClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(mapping, descriptor)
    {
    }

    public override IStorageOperation Insert(TDoc document, IMartenSession session, string tenant)
        => new UnversionedClosedShapeInsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor);

    public override IStorageOperation Update(TDoc document, IMartenSession session, string tenant)
        => new UnversionedClosedShapeUpdateOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor);

    public override IStorageOperation Upsert(TDoc document, IMartenSession session, string tenant)
        => new UnversionedClosedShapeUpsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, OperationRole.Upsert);

    public override IStorageOperation Overwrite(TDoc document, IMartenSession session, string tenant)
        => new UnversionedClosedShapeOverwriteOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor);

    public override IStorageOperation OverwriteProjected(TDoc document, string tenant)
        => new UnversionedClosedShapeOverwriteOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor);

    public override ISelector BuildSelector(IMartenSession session)
        => _descriptor.HierarchyMapping is not null
            ? new HierarchicalUnversionedClosedShapeDirtyTrackingSelector<TDoc, TId>(session, _descriptor)
            : new FlatUnversionedClosedShapeDirtyTrackingSelector<TDoc, TId>(session, _descriptor);
}
