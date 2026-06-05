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

    protected LightweightClosedShapeStorage(DocumentMapping mapping, DocumentStorageDescriptor<TDoc, TId> descriptor)
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

    // #4667 Phase 2 — session-free projection load. Opens a fresh connection
    // from the supplied database and deserializes the data column directly,
    // bypassing the session-aware BuildSelector path that writes versions /
    // ItemMap / ChangeTrackers per row. Shared with IdentityMap / DirtyChecked.
    public override Task<TDoc?> LoadProjectedAsync(TId id, IMartenDatabase database, string tenantId, CancellationToken token)
        => ClosedShapeProjectionLoader<TDoc, TId>.LoadAsync(
            BuildLoadCommand(id, tenantId), _descriptor, _mapping.StoreOptions.Serializer(), database, token);

    public override Task<IReadOnlyList<TDoc>> LoadManyProjectedAsync(TId[] ids, IMartenDatabase database, string tenantId, CancellationToken token)
        => ClosedShapeProjectionLoader<TDoc, TId>.LoadManyAsync(
            BuildLoadManyCommand(ids, tenantId), _descriptor, _mapping.StoreOptions.Serializer(), database, token);
}
