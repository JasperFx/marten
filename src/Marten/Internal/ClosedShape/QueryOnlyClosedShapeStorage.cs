#nullable enable
using System;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M2+M4): hand-written, closed-shape
/// <see cref="QueryOnlyDocumentStorage{T, TId}"/> for any
/// <typeparamref name="TId"/>. QueryOnly is the read-only tracking
/// mode — sessions opened as <c>IQuerySession</c> get this storage.
/// </summary>
/// <remarks>
/// <see cref="QueryOnlyDocumentStorage{T, TId}"/> seals Store / Eject /
/// LoadAsync / LoadManyAsync as no-ops or read-only paths. Insert /
/// Update / Upsert / Overwrite remain abstract — they'd never be called
/// against a query session, but we still have to provide bodies. We
/// throw if anyone reaches them.
/// </remarks>
public sealed class QueryOnlyClosedShapeStorage<TDoc, TId>: QueryOnlyDocumentStorage<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    private readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;

    // #4828: expose the descriptor's dialect as the base storage dialect (Strategy).
    protected override IStorageDialect Dialect => _descriptor.Dialect;

    public QueryOnlyClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(mapping)
    {
        _descriptor = descriptor;
    }

    public override TId Identity(TDoc document)
        => _descriptor.Identification.Identity(document);

    public override TId AssignIdentity(TDoc document, string tenantId, IStorageDatabase database)
        => _descriptor.Identification.AssignIfMissing(document, database);

    public override object RawIdentityValue(TId id)
        => _descriptor.Identification.ToRawSqlValue(id);

    public override System.Data.Common.DbParameter BuildManyIdParameter(TId[] ids)
        => Dialect.CreateIdArrayParameter(
            System.Array.ConvertAll(ids, id => _descriptor.Identification.ToRawSqlValue(id)),
            _descriptor.Identification.RawSqlType);

    public override Weasel.Storage.IStorageOperation Insert(TDoc document, IStorageSession session, string tenant)
        => throw new NotSupportedException("QueryOnly storage doesn't support Insert.");

    public override Weasel.Storage.IStorageOperation Update(TDoc document, IStorageSession session, string tenant)
        => throw new NotSupportedException("QueryOnly storage doesn't support Update.");

    public override Weasel.Storage.IStorageOperation Upsert(TDoc document, IStorageSession session, string tenant)
        => throw new NotSupportedException("QueryOnly storage doesn't support Upsert.");

    public override Weasel.Storage.IStorageOperation Overwrite(TDoc document, IStorageSession session, string tenant)
        => throw new NotSupportedException("QueryOnly storage doesn't support Overwrite.");

    public override Weasel.Storage.IStorageOperation OverwriteProjected(TDoc document, string tenant)
        => throw new NotSupportedException("QueryOnly storage doesn't support OverwriteProjected.");

    // #4667 — projection write paths aren't reachable from query sessions.
    public override Weasel.Storage.IStorageOperation UpsertProjected(TDoc document, string tenant)
        => throw new NotSupportedException("QueryOnly storage doesn't support UpsertProjected.");

    public override Weasel.Storage.IStorageOperation InsertProjected(TDoc document, string tenant)
        => throw new NotSupportedException("QueryOnly storage doesn't support InsertProjected.");

    public override Weasel.Storage.IStorageOperation UpdateProjected(TDoc document, string tenant)
        => throw new NotSupportedException("QueryOnly storage doesn't support UpdateProjected.");

    // #4667 Phase 2 — QueryOnly storages aren't used by the projection read
    // path; ProjectionStorage holds a writeable storage instance for the
    // projected document type, not a QueryOnly one.
    public override System.Threading.Tasks.Task<TDoc?> LoadProjectedAsync(TId id, IStorageDatabase database, string tenantId, System.Threading.CancellationToken token)
        => throw new NotSupportedException("QueryOnly storage doesn't support LoadProjectedAsync.");

    public override System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<TDoc>> LoadManyProjectedAsync(TId[] ids, IStorageDatabase database, string tenantId, System.Threading.CancellationToken token)
        => throw new NotSupportedException("QueryOnly storage doesn't support LoadManyProjectedAsync.");

    public override ISelector BuildSelector(IStorageSession session)
        // #4659 Phase 2: pick the Flat / Hierarchical selector ONCE per
        // query — neither selector class branches on ResolveDocumentType per
        // row.
        => _descriptor.ResolveDocumentType is not null
            ? new HierarchicalClosedShapeQueryOnlySelector<TDoc, TId>(session, _descriptor)
            : new FlatClosedShapeQueryOnlySelector<TDoc, TId>(session, _descriptor);
}
