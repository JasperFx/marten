#nullable enable
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.Selectors;
using Marten.Schema;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Numeric</c> closed-shape Lightweight storage.
/// Factories construct <c>Numeric…</c> operation leaves and pass the
/// session's typed long revision tracker; BuildSelector returns the
/// numeric read selector. #4659 leaf.
/// </summary>
internal sealed class NumericLightweightClosedShapeStorage<TDoc, TId>: LightweightClosedShapeStorage<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    public NumericLightweightClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(mapping, descriptor)
    {
    }

    public override IStorageOperation Insert(TDoc document, IMartenSession session, string tenant)
        => new NumericClosedShapeInsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.RevisionsFor<TDoc, TId>());

    public override IStorageOperation Update(TDoc document, IMartenSession session, string tenant)
        => new NumericClosedShapeUpdateOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.RevisionsFor<TDoc, TId>());

    public override IStorageOperation Upsert(TDoc document, IMartenSession session, string tenant)
        => new NumericClosedShapeUpsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, OperationRole.Upsert, session.Versions.RevisionsFor<TDoc, TId>());

    public override IStorageOperation Overwrite(TDoc document, IMartenSession session, string tenant)
        => new NumericClosedShapeOverwriteOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, session.Versions.RevisionsFor<TDoc, TId>());

    public override IStorageOperation OverwriteProjected(TDoc document, string tenant)
        // #4658: projection path passes null tracker so it doesn't poison
        // the session's numeric-revision map for the projected doc.
        => new NumericClosedShapeOverwriteOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, null);

    public override ISelector BuildSelector(IMartenSession session)
        => _descriptor.HierarchyMapping is not null
            ? new HierarchicalNumericClosedShapeLightweightSelector<TDoc, TId>(session, _descriptor)
            : new FlatNumericClosedShapeLightweightSelector<TDoc, TId>(session, _descriptor);
}
