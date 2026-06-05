#nullable enable
using System.Data.Common;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Off</c> DirtyTracking selector. CaptureVersion is
/// a no-op. #4659 leaf.
/// </summary>
internal sealed class UnversionedClosedShapeDirtyTrackingSelector<T, TId>: ClosedShapeDirtyTrackingSelector<T, TId>
    where T : notnull
    where TId : notnull
{
    public UnversionedClosedShapeDirtyTrackingSelector(IMartenSession session, DocumentStorageDescriptor<T, TId> descriptor)
        : base(session, descriptor)
    {
    }

    protected override void CaptureVersion(DbDataReader reader, TId id) { /* no-op */ }
}
