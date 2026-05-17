#nullable enable
using System;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;
using Marten.Schema;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M2): hand-written, closed-shape
/// <see cref="DirtyCheckedDocumentStorage{T, TId}"/> for Guid-id documents
/// with sequential-GUID identity. Selected when a session opens with
/// <c>DocumentTracking.DirtyTracking</c>.
/// </summary>
/// <remarks>
/// <see cref="DirtyCheckedDocumentStorage{T, TId}"/> inherits identity-map
/// semantics from <see cref="IdentityMapDocumentStorage{T, TId}"/>. The
/// dirty-tracking divergence lives in the selector — every loaded doc
/// gets a <c>ChangeTracker&lt;T&gt;</c> registered on the session so
/// <c>SaveChangesAsync</c> can dirty-check it.
/// </remarks>
public sealed class DirtyCheckedSequentialGuidStorage<TDoc>: DirtyCheckedDocumentStorage<TDoc, Guid>
    where TDoc : notnull
{
    private readonly DocumentStorageDescriptor<TDoc, Guid> _descriptor;

    public DirtyCheckedSequentialGuidStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, Guid> descriptor)
        : base(mapping)
    {
        _descriptor = descriptor;
    }

    public override Guid Identity(TDoc document)
        => _descriptor.Identification.Identity(document);

    public override Guid AssignIdentity(TDoc document, string tenantId, IMartenDatabase database)
        => _descriptor.Identification.AssignIfMissing(document, database);

    public override IStorageOperation Insert(TDoc document, IMartenSession session, string tenant)
        => Upsert(document, session, tenant);

    public override IStorageOperation Update(TDoc document, IMartenSession session, string tenant)
        => Upsert(document, session, tenant);

    public override IStorageOperation Upsert(TDoc document, IMartenSession session, string tenant)
        => new ClosedShapeUpsertOperation<TDoc, Guid>(document, Identity(document), _descriptor, OperationRole.Upsert);

    public override IStorageOperation Overwrite(TDoc document, IMartenSession session, string tenant)
        => throw new NotSupportedException(
            $"{nameof(DirtyCheckedSequentialGuidStorage<TDoc>)} doesn't implement Overwrite — out of spike scope. " +
            "Add when wiring optimistic concurrency / revisions (M3).");

    public override ISelector BuildSelector(IMartenSession session)
        => new ClosedShapeDirtyTrackingSelector<TDoc, Guid>(session, _descriptor);
}
