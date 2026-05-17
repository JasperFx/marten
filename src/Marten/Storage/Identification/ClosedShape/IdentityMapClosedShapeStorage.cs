#nullable enable
using System;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;
using Marten.Schema;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M2+M4): hand-written, closed-shape
/// <see cref="IdentityMapDocumentStorage{T, TId}"/> for any
/// <typeparamref name="TId"/>. Selected when a session opens with
/// <c>DocumentTracking.IdentityOnly</c>.
/// </summary>
public sealed class IdentityMapClosedShapeStorage<TDoc, TId>: IdentityMapDocumentStorage<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;

    public IdentityMapClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(mapping)
    {
        _descriptor = descriptor;
    }

    public override TId Identity(TDoc document)
        => _descriptor.Identification.Identity(document);

    public override TId AssignIdentity(TDoc document, string tenantId, IMartenDatabase database)
        => _descriptor.Identification.AssignIfMissing(document, database);

    public override IStorageOperation Insert(TDoc document, IMartenSession session, string tenant)
        => new ClosedShapeInsertOperation<TDoc, TId>(document, Identity(document), _descriptor);

    public override IStorageOperation Update(TDoc document, IMartenSession session, string tenant)
        => new ClosedShapeUpdateOperation<TDoc, TId>(document, Identity(document), _descriptor);

    public override IStorageOperation Upsert(TDoc document, IMartenSession session, string tenant)
        => new ClosedShapeUpsertOperation<TDoc, TId>(document, Identity(document), _descriptor, OperationRole.Upsert);

    public override IStorageOperation Overwrite(TDoc document, IMartenSession session, string tenant)
        => throw new NotSupportedException(
            $"{nameof(IdentityMapClosedShapeStorage<TDoc, TId>)} doesn't implement Overwrite — concurrency variants land in a later spike commit.");

    public override ISelector BuildSelector(IMartenSession session)
        => new ClosedShapeIdentityMapSelector<TDoc, TId>(session, _descriptor);
}
