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

    public QueryOnlyClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
        : base(mapping)
    {
        _descriptor = descriptor;
    }

    public override TId Identity(TDoc document)
        => _descriptor.Identification.Identity(document);

    public override TId AssignIdentity(TDoc document, string tenantId, IMartenDatabase database)
        => _descriptor.Identification.AssignIfMissing(document, database);

    public override object RawIdentityValue(TId id)
        => _descriptor.Identification.ToRawSqlValue(id);

    public override Npgsql.NpgsqlParameter BuildManyIdParameter(TId[] ids)
        => ClosedShapeIdHelpers.BuildManyIdParameter(ids, _descriptor.Identification);

    public override IStorageOperation Insert(TDoc document, IMartenSession session, string tenant)
        => throw new NotSupportedException("QueryOnly storage doesn't support Insert.");

    public override IStorageOperation Update(TDoc document, IMartenSession session, string tenant)
        => throw new NotSupportedException("QueryOnly storage doesn't support Update.");

    public override IStorageOperation Upsert(TDoc document, IMartenSession session, string tenant)
        => throw new NotSupportedException("QueryOnly storage doesn't support Upsert.");

    public override IStorageOperation Overwrite(TDoc document, IMartenSession session, string tenant)
        => throw new NotSupportedException("QueryOnly storage doesn't support Overwrite.");

    public override ISelector BuildSelector(IMartenSession session)
        => new ClosedShapeQueryOnlySelector<TDoc, TId>(session, _descriptor);
}
