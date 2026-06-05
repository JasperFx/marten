#nullable enable
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// Closed-shape <see cref="DirtyCheckedDocumentStorage{T, TId}"/> base.
/// Sealed concurrency-mode leaves provide the write factories +
/// <c>BuildSelector</c>. See <see cref="LightweightClosedShapeStorage{TDoc,TId}"/>
/// for the rationale (#4659).
/// </summary>
public abstract class DirtyCheckedClosedShapeStorage<TDoc, TId>: DirtyCheckedDocumentStorage<TDoc, TId>
    where TDoc : notnull
    where TId : notnull
{
    protected readonly DocumentStorageDescriptor<TDoc, TId> _descriptor;

    protected DirtyCheckedClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
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
}
