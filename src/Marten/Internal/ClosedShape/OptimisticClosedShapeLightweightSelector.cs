#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Optimistic</c> Lightweight selector — concurrency-
/// mode intermediate. CaptureVersion writes the row's mt_version (Guid)
/// into the session's per-type version dict; sealed Flat / Hierarchical
/// subclasses provide <c>ReadDocument</c> / <c>ReadDocumentAsync</c>
/// (#4659 Phase 2).
/// </summary>
internal abstract class OptimisticClosedShapeLightweightSelector<T, TId>: ClosedShapeLightweightSelector<T, TId>
    where T : notnull
    where TId : notnull
{
    private readonly Dictionary<TId, Guid> _versions;

    protected OptimisticClosedShapeLightweightSelector(IMartenSession session, DocumentStorageDescriptor<T, TId> descriptor)
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
