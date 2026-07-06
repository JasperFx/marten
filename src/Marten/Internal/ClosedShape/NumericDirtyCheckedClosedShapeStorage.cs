#nullable enable
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.Selectors;
using Marten.Schema;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Numeric</c> closed-shape DirtyChecked storage. #4659 leaf.
/// </summary>
internal sealed class NumericDirtyCheckedClosedShapeStorage<TDoc, TId>: DirtyCheckedClosedShapeStorage<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    public NumericDirtyCheckedClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(mapping, descriptor)
    {
    }

    public override Weasel.Storage.IStorageOperation Insert(TDoc document, IStorageSession session, string tenant)
        => new NumericClosedShapeInsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.RevisionsFor<TDoc, TId>());

    public override Weasel.Storage.IStorageOperation Update(TDoc document, IStorageSession session, string tenant)
        => new NumericClosedShapeUpdateOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.RevisionsFor<TDoc, TId>());

    public override Weasel.Storage.IStorageOperation Upsert(TDoc document, IStorageSession session, string tenant)
        => new NumericClosedShapeUpsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, OperationRole.Upsert, session.Versions.RevisionsFor<TDoc, TId>());

    public override Weasel.Storage.IStorageOperation Overwrite(TDoc document, IStorageSession session, string tenant)
        => new NumericClosedShapeOverwriteOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.RevisionsFor<TDoc, TId>());

    public override Weasel.Storage.IStorageOperation OverwriteProjected(TDoc document, string tenant)
        => new NumericClosedShapeOverwriteOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, null);

    // #4667 — null revision tracker; see Lightweight peer for semantics.
    public override Weasel.Storage.IStorageOperation UpsertProjected(TDoc document, string tenant)
        => new NumericClosedShapeUpsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, OperationRole.Upsert, null);

    public override Weasel.Storage.IStorageOperation InsertProjected(TDoc document, string tenant)
        => new NumericClosedShapeInsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, null);

    public override Weasel.Storage.IStorageOperation UpdateProjected(TDoc document, string tenant)
        => new NumericClosedShapeUpdateOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, null);

    public override ISelector BuildSelector(IStorageSession session)
        => _descriptor.ResolveDocumentType is not null
            ? new HierarchicalNumericClosedShapeDirtyTrackingSelector<TDoc, TId>(session, _descriptor)
            : new FlatNumericClosedShapeDirtyTrackingSelector<TDoc, TId>(session, _descriptor);
}
