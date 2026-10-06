#nullable enable

namespace Marten.Services.BatchQuerying;

/// <summary>
///     Marks a batch query handler that brackets its own SQL in <c>begin transaction … end</c>, and so
///     cannot share a batch with anything that depends on the session's transaction surviving the batch.
/// </summary>
/// <remarks>
///     Implemented by the three <c>FetchAsyncPlan</c> handlers that emit
///     <c>begin transaction isolation level repeatable read read only</c> … <c>end</c> so their reads
///     share one snapshot: the <b>non-exclusive</b> <c>FetchForWriting</c>, its expected-version
///     overload (#5604), and <c>FetchLatest</c> (#5604).
///     <para>
///     Deliberately keyed off the handler rather than off <c>ProjectionLifecycle.Async</c>: the handler
///     is the thing that actually decides whether to emit the SQL, and since #5611 that decision also
///     depends on whether the session already holds a transaction. A lifecycle check would be a proxy
///     that drifts the moment either side changes.
///     </para>
/// </remarks>
internal interface IOpensItsOwnTransaction
{
    /// <summary>True when this handler's SQL will open and commit its own transaction.</summary>
    bool OpensItsOwnTransaction { get; }

    /// <summary>
    ///     The call the user actually wrote — <c>FetchForWriting</c> or <c>FetchLatest</c> — for the
    ///     messages that refuse a batch.
    /// </summary>
    /// <remarks>
    ///     Carried by the handler rather than derived from its type name. The handler types are named
    ///     after Marten's internals (<c>ForUpdateQueryHandler</c> is the NOT-for-update case; the
    ///     <c>FetchLatest</c> one is just <c>QueryHandler</c>), so a type-name mapping would be both
    ///     unreadable and easy to get silently wrong.
    /// </remarks>
    string CallName { get; }
}
