#nullable enable
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;
using Marten.Schema;

namespace Marten.Storage.Identification.ClosedShape;

/// <summary>
/// W3 spike (M2+M4+M7): hand-written, closed-shape
/// <see cref="LightweightDocumentStorage{T, TId}"/> for any
/// <typeparamref name="TId"/>. Composes
/// <see cref="DocumentStorageDescriptor{TDoc, TId}"/> (SQL + metadata
/// binders) and <see cref="IIdentification{TDoc, TId}"/> (identity
/// strategy). The closed-shape JIT specialization happens at
/// construction time per <c>(TDoc, TId)</c> closure — no runtime
/// branching after that.
/// </summary>
/// <remarks>
/// <para>
/// One cell of the planned W3 matrix: Lightweight + any id type + any
/// concurrency mode + no revisions + no tenancy + no hierarchical.
/// </para>
/// <para>
/// Inheriting <see cref="LightweightDocumentStorage{T, TId}"/> picks up
/// Store / Eject / LoadAsync / LoadManyAsync. What we hand-write here:
/// Identity / AssignIdentity (via the descriptor's
/// <see cref="IIdentification{TDoc, TId}"/>), Insert / Update / Upsert /
/// Overwrite (return the corresponding closed-shape operation), and
/// BuildSelector (returns <see cref="ClosedShapeLightweightSelector{TDoc, TId}"/>).
/// </para>
/// </remarks>
public sealed class LightweightClosedShapeStorage<TDoc, TId>: LightweightDocumentStorage<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;

    public LightweightClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(mapping)
    {
        _descriptor = descriptor;
    }

    public override TId Identity(TDoc document)
        => _descriptor.Identification.Identity(document);

    public override TId AssignIdentity(TDoc document, string tenantId, IMartenDatabase database)
        => _descriptor.Identification.AssignIfMissing(document, database);

    // M15: strong-typed wrappers need to bind the inner primitive
    // (Guid / int / long / string) rather than the wrapper struct.
    public override object RawIdentityValue(TId id)
        => _descriptor.Identification.ToRawSqlValue(id);

    public override Npgsql.NpgsqlParameter BuildManyIdParameter(TId[] ids)
        => ClosedShapeIdHelpers.BuildManyIdParameter(ids, _descriptor.Identification);

    public override IStorageOperation Insert(TDoc document, IMartenSession session, string tenant)
        => new ClosedShapeInsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, VersionsFor(session), RevisionsFor(session));

    public override IStorageOperation Update(TDoc document, IMartenSession session, string tenant)
        => new ClosedShapeUpdateOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, VersionsFor(session), RevisionsFor(session));

    public override IStorageOperation Upsert(TDoc document, IMartenSession session, string tenant)
        => new ClosedShapeUpsertOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, OperationRole.Upsert, VersionsFor(session), RevisionsFor(session));

    public override IStorageOperation Overwrite(TDoc document, IMartenSession session, string tenant)
        => new ClosedShapeOverwriteOperation<TDoc, TId>(document, Identity(document), tenant, _descriptor, VersionsFor(session), RevisionsFor(session));

    public override ISelector BuildSelector(IMartenSession session)
        => new ClosedShapeLightweightSelector<TDoc, TId>(session, _descriptor);

    private System.Collections.Generic.Dictionary<TId, System.Guid>? VersionsFor(IMartenSession session)
        => _descriptor.ConcurrencyMode == ConcurrencyMode.Optimistic
            ? session.Versions.ForType<TDoc, TId>()
            : null;

    private System.Collections.Generic.Dictionary<TId, long>? RevisionsFor(IMartenSession session)
        => _descriptor.ConcurrencyMode == ConcurrencyMode.Numeric
            ? session.Versions.RevisionsFor<TDoc, TId>()
            : null;
}
