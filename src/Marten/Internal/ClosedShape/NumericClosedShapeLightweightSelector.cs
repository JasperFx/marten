#nullable enable
using System.Collections.Generic;
using System.Data.Common;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Numeric</c> Lightweight selector. Captures the
/// row's mt_version (long) into the session's per-type revision dict so
/// subsequent updates can supply it as the expected revision. #4659 leaf.
/// </summary>
internal sealed class NumericClosedShapeLightweightSelector<T, TId>: ClosedShapeLightweightSelector<T, TId>
    where T : notnull
    where TId : notnull
{
    private readonly Dictionary<TId, long> _revisions;

    public NumericClosedShapeLightweightSelector(IMartenSession session, DocumentStorageDescriptor<T, TId> descriptor)
        : base(session, descriptor)
    {
        _revisions = session.Versions.RevisionsFor<T, TId>();
    }

    protected override void CaptureVersion(DbDataReader reader, TId id)
    {
        var versionIndex = _descriptor.VersionReadIndex;
        if (versionIndex < 0) return;
        var versionOrdinal = FirstMetadataColumn + versionIndex;
        if (reader.IsDBNull(versionOrdinal)) return;

        _revisions[id] = reader.GetFieldValue<long>(versionOrdinal);
    }
}
