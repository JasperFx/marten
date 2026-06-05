#nullable enable
using System.Collections.Generic;
using System.Data.Common;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Numeric</c> IdentityMap selector. CaptureVersion
/// writes the row's mt_version (long) into the session's per-type
/// revision dict. #4659 leaf.
/// </summary>
internal sealed class NumericClosedShapeIdentityMapSelector<T, TId>: ClosedShapeIdentityMapSelector<T, TId>
    where T : notnull
    where TId : notnull
{
    private readonly Dictionary<TId, long> _revisions;

    public NumericClosedShapeIdentityMapSelector(IMartenSession session, DocumentStorageDescriptor<T, TId> descriptor)
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
