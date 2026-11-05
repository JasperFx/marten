#nullable enable
using System;

namespace Marten.Exceptions;

/// <summary>
///     Thrown by a <b>batched</b> <c>FetchForWriting</c>, <c>FetchForExclusiveWriting</c> or
///     <c>FetchLatest</c> for an aggregate projected with <c>ProjectionLifecycle.Async</c>, when the
///     stream's snapshot document went BACKWARDS between that batch's event read and its snapshot read.
///     Retry the batch.
/// </summary>
/// <remarks>
///     <para>
///         An Async fetch reads the events after the snapshot first and the snapshot document second, as
///         separate statements that each see the database as it is when they start. The async daemon only
///         moves a snapshot forward, which that order is safe against. A snapshot moves backwards only when
///         it is deleted or rebuilt, and then the events between the old and the new snapshot version were
///         read by neither statement. That is detected rather than answered with a wrong aggregate.
///     </para>
///     <para>
///         Outside a batch Marten handles this itself: the fetch is simply redone, and callers never see
///         this exception. Inside a batch there is nothing to redo — a batch handler is handed one reader
///         and cannot issue another statement — so the only honest answers are a wrong aggregate or this.
///         For a fetch whose whole purpose is to decide what to write, an exception you can retry is the
///         better one.
///     </para>
/// </remarks>
public class SnapshotRaceException: MartenException
{
    internal SnapshotRaceException(Type aggregateType, object id): base(
        $"The snapshot of {aggregateType.FullName} '{id}' went backwards while this batch was reading it, as it does "
        + "while a projection is rebuilt, so the snapshot and the events after it disagree. Retry the batch, or fetch "
        + "outside a batch, where Marten retries for you.")
    {
        AggregateType = aggregateType;
        Id = id;
    }

    /// <summary>The aggregate type being fetched.</summary>
    public Type AggregateType { get; }

    /// <summary>The identity of the stream being fetched.</summary>
    public object Id { get; }
}
