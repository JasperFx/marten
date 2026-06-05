#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Optimistic</c> DirtyTracking selector —
/// concurrency-mode intermediate. CaptureVersion writes Guid into the
/// session's per-type version dict. Sealed Flat / Hierarchical
/// subclasses provide ReadDocument. #4659 Phase 2.
/// </summary>
internal abstract class OptimisticClosedShapeDirtyTrackingSelector<T, TId>: ClosedShapeDirtyTrackingSelector<T, TId>
    where T : notnull
    where TId : notnull
{
    private readonly Dictionary<TId, Guid> _versions;

    protected OptimisticClosedShapeDirtyTrackingSelector(IMartenSession session, DocumentStorageDescriptor<T, TId> descriptor)
        : base(session, descriptor)
    {
        _versions = session.Versions.ForType<T, TId>();
    }

    protected sealed override void CaptureVersion(DbDataReader reader, TId id)
    {
        var versionIndex = _descriptor.VersionReadIndex;
        if (versionIndex < 0) return;
        var versionOrdinal = FirstMetadataColumn + versionIndex;
        if (reader.IsDBNull(versionOrdinal)) return;

        _versions[id] = reader.GetFieldValue<Guid>(versionOrdinal);
    }
}
