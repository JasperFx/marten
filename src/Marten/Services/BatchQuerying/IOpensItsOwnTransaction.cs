#nullable enable

namespace Marten.Services.BatchQuerying;

/// <summary>
///     Marks a batch query handler that brackets its own SQL in <c>begin transaction … end</c>, and so
///     cannot share a batch with anything that depends on the session's transaction surviving the batch.
/// </summary>
/// <remarks>
///     Implemented by <c>FetchAsyncPlan.ForUpdateQueryHandler</c>, which emits
///     <c>begin transaction isolation level repeatable read read only</c> … <c>end</c> for a
///     <b>non-exclusive</b> <c>FetchForWriting</c> so its two reads share one snapshot.
///     <para>
///     Deliberately keyed off the handler rather than off <c>ProjectionLifecycle.Async</c>: the SQL is
///     emitted by <c>if (!_forUpdate)</c> inside the handler, so asking the handler is asking the thing
///     that actually decides. A lifecycle check would be a proxy that drifts the moment either side
///     changes.
///     </para>
/// </remarks>
internal interface IOpensItsOwnTransaction
{
    /// <summary>True when this handler's SQL will open and commit its own transaction.</summary>
    bool OpensItsOwnTransaction { get; }
}
