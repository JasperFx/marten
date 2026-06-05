#nullable enable
using System.Data.Common;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Off</c> Lightweight selector. No version/revision
/// tracker — CaptureVersion is a no-op. #4659 leaf.
/// </summary>
internal sealed class UnversionedClosedShapeLightweightSelector<T, TId>: ClosedShapeLightweightSelector<T, TId>
    where T : notnull
    where TId : notnull
{
    public UnversionedClosedShapeLightweightSelector(IMartenSession session, DocumentStorageDescriptor<T, TId> descriptor)
        : base(session, descriptor)
    {
    }

    protected override void CaptureVersion(DbDataReader reader, TId id)
    {
        // Off-mode: nothing to capture. The mt_version column may or may
        // not be present in ReadBinders (depends on the user's mapping)
        // but the session has no tracker to push it into.
    }
}
