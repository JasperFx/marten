#nullable enable
using System.Collections.Generic;
using System.Data.Common;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Numeric</c> Lightweight selector — concurrency-
/// mode intermediate. CaptureVersion writes the row's mt_version (long)
/// into the session's per-type revision dict; sealed Flat / Hierarchical
/// subclasses provide <c>ReadDocument</c> / <c>ReadDocumentAsync</c>
/// (#4659 Phase 2).
/// </summary>
internal abstract class NumericClosedShapeLightweightSelector<T, TId>: ClosedShapeLightweightSelector<T, TId>
    where T : notnull
    where TId : notnull
{
    private readonly Dictionary<TId, long> _revisions;

    protected NumericClosedShapeLightweightSelector(IStorageSession session, DocumentStorageDescriptor<T, TId> descriptor)
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
