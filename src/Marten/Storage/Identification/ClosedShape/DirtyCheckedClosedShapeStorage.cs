#nullable enable
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;
using Marten.Schema;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M2+M4+M7): hand-written, closed-shape
/// <see cref="DirtyCheckedDocumentStorage{T, TId}"/> for any
/// <typeparamref name="TId"/>. Selected when a session opens with
/// <c>DocumentTracking.DirtyTracking</c>.
/// </summary>
public sealed class DirtyCheckedClosedShapeStorage<TDoc, TId>: DirtyCheckedDocumentStorage<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;

    public DirtyCheckedClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(mapping)
    {
        _descriptor = descriptor;
    }

    public override TId Identity(TDoc document)
        => _descriptor.Identification.Identity(document);

    public override TId AssignIdentity(TDoc document, string tenantId, IMartenDatabase database)
        => _descriptor.Identification.AssignIfMissing(document, database);

    public override IStorageOperation Insert(TDoc document, IMartenSession session, string tenant)
        => new ClosedShapeInsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, VersionsFor(session));

    public override IStorageOperation Update(TDoc document, IMartenSession session, string tenant)
        => new ClosedShapeUpdateOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, VersionsFor(session));

    public override IStorageOperation Upsert(TDoc document, IMartenSession session, string tenant)
        => new ClosedShapeUpsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, OperationRole.Upsert, VersionsFor(session));

    public override IStorageOperation Overwrite(TDoc document, IMartenSession session, string tenant)
        => new ClosedShapeOverwriteOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, VersionsFor(session));

    public override ISelector BuildSelector(IMartenSession session)
        => new ClosedShapeDirtyTrackingSelector<TDoc, TId>(session, _descriptor);

    private System.Collections.Generic.Dictionary<TId, System.Guid>? VersionsFor(IMartenSession session)
        => _descriptor.ConcurrencyMode == ConcurrencyMode.Off
            ? null
            : session.Versions.ForType<TDoc, TId>();
}
