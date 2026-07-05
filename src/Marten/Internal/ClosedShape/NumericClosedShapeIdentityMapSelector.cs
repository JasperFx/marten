#nullable enable
using System.Collections.Generic;
using System.Data.Common;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Numeric</c> IdentityMap selector — concurrency-
/// mode intermediate. CaptureVersion writes long into the session's
/// per-type revision dict. Sealed Flat / Hierarchical subclasses
/// provide ReadDocument. #4659 Phase 2.
/// </summary>
internal abstract class NumericClosedShapeIdentityMapSelector<T, TId>: ClosedShapeIdentityMapSelector<T, TId>
    where T : notnull
    where TId : notnull
{
    private readonly Dictionary<TId, long> _revisions;

    protected NumericClosedShapeIdentityMapSelector(IStorageSession session, DocumentStorageDescriptor<T, TId> descriptor)
        : base(session, descriptor)
    {
        _revisions = session.Versions.RevisionsFor<T, TId>();
    }

    protected sealed override void CaptureVersion(DbDataReader reader, TId id)
    {
        var versionIndex = _descriptor.VersionReadIndex;
        if (versionIndex < 0) return;
        var versionOrdinal = FirstMetadataColumn + versionIndex;
        if (reader.IsDBNull(versionOrdinal)) return;

        _revisions[id] = reader.GetFieldValue<long>(versionOrdinal);
    }
}
