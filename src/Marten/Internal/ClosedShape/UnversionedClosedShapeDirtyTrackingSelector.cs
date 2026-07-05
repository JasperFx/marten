#nullable enable
using System.Data.Common;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Off</c> DirtyTracking selector — concurrency-mode
/// intermediate. CaptureVersion is a no-op. Sealed Flat / Hierarchical
/// subclasses provide ReadDocument. #4659 Phase 2.
/// </summary>
internal abstract class UnversionedClosedShapeDirtyTrackingSelector<T, TId>: ClosedShapeDirtyTrackingSelector<T, TId>
    where T : notnull
    where TId : notnull
{
    protected UnversionedClosedShapeDirtyTrackingSelector(IStorageSession session, DocumentStorageDescriptor<T, TId> descriptor)
        : base(session, descriptor)
    {
    }

    protected sealed override void CaptureVersion(DbDataReader reader, TId id) { /* no-op */ }
}
