namespace Marten;

/// <summary>
/// Governs what <see cref="IQuerySession.QueryForNonStaleData{T}(System.TimeSpan, NonStaleDataTimeoutMode)"/>
/// does when the asynchronous projection it is querying cannot catch up to the event store high-water
/// mark within the supplied timeout.
/// </summary>
public enum NonStaleDataTimeoutMode
{
    /// <summary>
    /// Throw a <see cref="System.TimeoutException"/> when the projection does not reach the high-water
    /// mark within the timeout. This is the behavior of the single-argument
    /// <see cref="IQuerySession.QueryForNonStaleData{T}(System.TimeSpan)"/> overload.
    /// </summary>
    ThrowException,

    /// <summary>
    /// Return the latest available data even if it may be stale rather than throwing when the projection
    /// cannot catch up within the timeout. Useful when an unreachable high-water mark (e.g. a gap in
    /// mt_events_sequence left by a failed append) would otherwise make every query throw indefinitely,
    /// and serving slightly stale data is preferable to failing the request.
    /// </summary>
    ReturnStaleData
}
