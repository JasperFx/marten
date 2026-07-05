#nullable enable
using System.Data.Common;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// <c>ConcurrencyMode.Off</c> Lightweight selector — concurrency-mode
/// intermediate. CaptureVersion is a no-op; sealed Flat / Hierarchical
/// subclasses provide <c>ReadDocument</c> /
/// <c>ReadDocumentAsync</c> (#4659 Phase 2).
/// </summary>
internal abstract class UnversionedClosedShapeLightweightSelector<T, TId>: ClosedShapeLightweightSelector<T, TId>
    where T : notnull
    where TId : notnull
{
    protected UnversionedClosedShapeLightweightSelector(IStorageSession session, DocumentStorageDescriptor<T, TId> descriptor)
        : base(session, descriptor)
    {
    }

    protected sealed override void CaptureVersion(DbDataReader reader, TId id)
    {
        // Off-mode: nothing to capture.
    }
}
