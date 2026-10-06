#nullable enable
using System;

namespace Marten.Exceptions;

/// <summary>
///     Thrown by a <b>batched</b> <c>FetchForExclusiveWriting</c> (or a batched <c>FetchForWriting</c> /
///     <c>FetchLatest</c> inside a caller-owned transaction) for an aggregate projected with
///     <c>ProjectionLifecycle.Async</c>, when the async daemon committed a snapshot of the same stream
///     in between that batch's snapshot read and its event read. Retry the batch.
/// </summary>
/// <remarks>
///     <para>
///         #5613. The event read finds its floor by re-reading the snapshot document's
///         <c>mt_version</c>. When the reads share one snapshot — which is what
///         <c>begin transaction isolation level repeatable read read only</c> … <c>end</c> buys a
///         non-exclusive fetch (#5604) — the two cannot disagree. An exclusive fetch cannot use that
///         bracket: it already owns a transaction, and its <c>for update</c> is on the
///         <c>mt_streams</c> row, which the daemon never touches. So the daemon can advance the
///         snapshot underneath the read, and the event read then excludes the very events the snapshot
///         read never saw — producing a null aggregate for a live stream, or a silently stale one.
///     </para>
///     <para>
///         Outside a batch Marten handles this itself: <c>FetchForWriting</c> simply redoes the fetch,
///         and callers never see this exception. Inside a batch there is nothing to redo — a batch
///         handler is handed one reader and cannot issue another statement — so the only honest answers
///         are a wrong aggregate or this. For a fetch whose whole purpose is to decide what to write,
///         an exception you can retry is the better one.
///     </para>
///     <para>
///         It is rare: the window is the microseconds between two statements of one batch, and it only
///         opens while the daemon is actively writing a snapshot for the very stream being fetched.
///         Retrying the batch is the fix; fetching outside a batch avoids it entirely.
///     </para>
/// </remarks>
public class SnapshotRaceException: MartenException
{
    internal SnapshotRaceException(Type aggregateType, object id): base(
        $"The async daemon committed a snapshot of {aggregateType.FullName} '{id}' while this batch was reading it, so the "
        + "snapshot and the events after it disagree. Retry the batch, or fetch outside a batch, where Marten retries "
        + "for you. See https://github.com/JasperFx/marten/issues/5613.")
    {
        AggregateType = aggregateType;
        Id = id;
    }

    /// <summary>The aggregate type being fetched.</summary>
    public Type AggregateType { get; }

    /// <summary>The identity of the stream being fetched.</summary>
    public object Id { get; }
}
