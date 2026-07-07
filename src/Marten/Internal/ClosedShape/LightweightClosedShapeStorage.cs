#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Closed-shape <see cref="LightweightDocumentStorage{T, TId}"/> base.
/// Holds the shared infrastructure (Identity / AssignIdentity /
/// RawIdentityValue / BuildManyIdParameter) common to every concurrency
/// flavor; concrete subclasses provide the Insert / Update / Upsert /
/// Overwrite factories + BuildSelector so the storage class is
/// monomorphic-by-construction per <c>(TDoc, TId, ConcurrencyMode)</c>
/// closure (#4659).
/// </summary>
/// <remarks>
/// <para>
/// Public sealed → public abstract. The class still exists as a public
/// type but cannot be instantiated directly; consumers go through
/// <see cref="ClosedShapeRegistration"/> which builds the right
/// concurrency-mode leaf. The W3 spike's <c>Use…ClosedShape</c>
/// extension helpers still work — the registration internals just
/// dispatch on <see cref="DocumentStorageDescriptor{TDoc,TId}.ConcurrencyMode"/>.
/// </para>
/// </remarks>
public abstract class LightweightClosedShapeStorage<TDoc, TId>: LightweightDocumentStorage<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    protected readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;

    // #4828: expose the descriptor's dialect as the base storage dialect (Strategy).
    protected override IStorageDialect Dialect => _descriptor.Dialect;

    protected LightweightClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(mapping)
    {
        _descriptor = descriptor;
    }

    public override TId Identity(TDoc document)
        => _descriptor.Identification.Identity(document);

    public override TId AssignIdentity(TDoc document, string tenantId, IStorageDatabase database)
        => _descriptor.Identification.AssignIfMissing(document, database);

    // M15: strong-typed wrappers need to bind the inner primitive
    // (Guid / int / long / string) rather than the wrapper struct.
    public override object RawIdentityValue(TId id)
        => _descriptor.Identification.ToRawSqlValue(id);

    public override System.Data.Common.DbParameter BuildManyIdParameter(TId[] ids)
        => Dialect.CreateIdArrayParameter(
            System.Array.ConvertAll(ids, id => _descriptor.Identification.ToRawSqlValue(id)),
            _descriptor.Identification.RawSqlType);

    // #4838 — Query<T>() / QueryAsync<T>(sql) / CreateBatchQuery() called from
    // inside a projection's Apply run on the async daemon's parallel slice
    // workers against ONE shared ProjectionDocumentSession. The version-capturing
    // Lightweight selectors do an unguarded get-or-add on session.Versions at
    // construction and per-row writes into the shared Dictionary<TId, …> — a
    // data race across workers. For the daemon session, hand back the Unversioned
    // selector instead: identical Lightweight row shape (id col 0, data col 1,
    // metadata 2+, so [Version]-mapped members are still set on the document),
    // but CaptureVersion is a no-op — the read path never touches session-shared
    // state, mirroring how LoadAsync routes through LoadProjectedAsync (#4667
    // Phase 3). No daemon path consumes query-captured versions: the
    // UseIdentityMapForAggregates aggregate cache reads/writes ItemMap via
    // ProjectionStorage (never session.Versions), and the projection write path
    // uses the *Projected operations with a null tracker. Event projections that
    // opt into EnableDocumentTrackingByIdentity run the session as IdentityOnly,
    // which selects the IdentityMap storages — not this Lightweight path — so
    // their document tracking is unaffected.
    private protected ISelector? tryBuildSessionFreeSelector(IStorageSession session)
    {
        if (session is not Events.Daemon.Internals.ProjectionDocumentSession)
        {
            return null;
        }

        return _descriptor.ResolveDocumentType is not null
            ? new HierarchicalUnversionedClosedShapeLightweightSelector<TDoc, TId>(session, _descriptor)
            : new FlatUnversionedClosedShapeLightweightSelector<TDoc, TId>(session, _descriptor);
    }

    // #4667 Phase 2 — session-free projection load. Opens a fresh connection
    // from the supplied database and deserializes the data column directly,
    // bypassing the session-aware BuildSelector path that writes versions /
    // ItemMap / ChangeTrackers per row. Shared with IdentityMap / DirtyChecked.
    public override Task<TDoc?> LoadProjectedAsync(TId id, IStorageDatabase database, string tenantId, CancellationToken token)
        => ClosedShapeProjectionLoader<TDoc, TId>.LoadAsync(
            BuildLoadCommand(id, tenantId), _descriptor, _descriptor.Serializer, database, token);

    public override Task<IReadOnlyList<TDoc>> LoadManyProjectedAsync(TId[] ids, IStorageDatabase database, string tenantId, CancellationToken token)
        => ClosedShapeProjectionLoader<TDoc, TId>.LoadManyAsync(
            BuildLoadManyCommand(ids, tenantId), _descriptor, _descriptor.Serializer, database, token);
}
