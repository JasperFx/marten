#nullable enable
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.Selectors;
using Marten.Schema;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Off</c> closed-shape IdentityMap storage. #4659 leaf.
/// </summary>
internal sealed class UnversionedIdentityMapClosedShapeStorage<TDoc, TId>: IdentityMapClosedShapeStorage<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    public UnversionedIdentityMapClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
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
        => new UnversionedClosedShapeIdentityMapSelector<TDoc, TId>(session, _descriptor);
}
