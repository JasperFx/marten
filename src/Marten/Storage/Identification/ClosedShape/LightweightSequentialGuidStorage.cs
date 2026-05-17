#nullable enable
using System;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;
using Marten.Schema;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike: hand-written, closed-shape <see cref="DocumentStorage{T, TId}"/>
/// subclass that proves the closed-shape pattern can be driven end-to-end
/// without runtime Roslyn codegen.
/// </summary>
/// <remarks>
/// <para>
/// One cell of the planned W3 matrix: Lightweight + Guid + no
/// concurrency + no revisions + no tenancy + no hierarchical. M1 adds
/// metadata-column support (<c>mt_version</c>, <c>mt_dotnet_type</c>,
/// <c>mt_last_modified</c>) via the
/// <see cref="DocumentStorageDescriptor{TDoc, TId}"/> seam — the same
/// storage class drives any subset of those columns based on the
/// document mapping's metadata flags.
/// </para>
/// <para>
/// Inheriting <see cref="LightweightDocumentStorage{T, TId}"/> picks up
/// Store / Eject / LoadAsync / LoadManyAsync. What we hand-write here:
/// Identity / AssignIdentity (via the descriptor's
/// <see cref="IIdentification{TDoc, TId}"/>), Insert / Update / Upsert
/// (return <see cref="ClosedShapeUpsertOperation{TDoc, TId}"/>),
/// Overwrite (throws — out of M1 scope), and BuildSelector (returns
/// <see cref="ClosedShapeLightweightSelector{T, TId}"/>).
/// </para>
/// </remarks>
public sealed class LightweightSequentialGuidStorage<TDoc>: LightweightDocumentStorage<TDoc, Guid>
    where TDoc : notnull
{
    private readonly DocumentStorageDescriptor<TDoc, Guid> _descriptor;

    public LightweightSequentialGuidStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, Guid> descriptor)
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
            $"{nameof(LightweightSequentialGuidStorage<TDoc>)} doesn't implement Overwrite — out of W3 spike scope. " +
            "Add when wiring optimistic concurrency / revisions (M3).");

    public override ISelector BuildSelector(IMartenSession session)
        => new ClosedShapeLightweightSelector<TDoc, Guid>(session.Serializer, _descriptor);
}
