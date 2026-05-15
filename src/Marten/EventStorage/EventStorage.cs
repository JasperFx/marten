#nullable enable
using System.Collections.Generic;
using JasperFx.Events;
using Marten.Internal.Operations;
using Marten.Linq.QueryHandlers;
using Marten.Services.BatchQuerying;

namespace Marten.EventStorage;

/// <summary>
/// Closed-shape event-storage abstraction. Three concrete implementations
/// ship — one per append-mode flavor — and exactly one is wired into a
/// <c>DocumentStore</c> at construction time based on the
/// <c>StoreOptions.Events.AppendMode</c> setting. Per-call dispatch is a
/// virtual call through this base; no runtime branching on append mode.
/// </summary>
/// <remarks>
/// <para>
/// W4 spike (#4404). The three implementations:
/// </para>
/// <list type="bullet">
///   <item><see cref="Rich.RichEventStorage{TId}"/> — full-mode per-event
///         inserts into <c>mt_events</c>. One <c>IStorageOperation</c> per
///         event. No RETURNING clause, no read-back into <c>IEvent</c>.
///         Metadata column variability via
///         <see cref="IEventMetadataBinder"/> array on the descriptor.</item>
///   <item><see cref="Quick.QuickEventStorage{TId}"/> — batch quick-append
///         via the <c>mt_quick_append_events</c> server function. One
///         <c>IStorageOperation</c> per stream batch. RETURNING array
///         carries the assigned versions + sequence numbers; the
///         operation's <c>Postprocess</c> hand-walks the events list to
///         write them back. Metadata binding is per-batch array
///         parameters — no binder abstraction because the shape (one
///         <c>NpgsqlDbType.Array</c> param per column) is uniform enough
///         to inline in source-gen output without combinatorial explosion.</item>
///   <item><see cref="QuickWithServerTimestamps.QuickWithServerTimestampsEventStorage{TId}"/>
///         — variant of Quick that asks the server for <c>now()</c>
///         timestamps and writes them back onto the events. Same shape
///         as Quick + one extra column.</item>
/// </list>
/// <para>
/// The "completely different implementations" framing is the key W4
/// decision over the earlier composite/binder-everywhere sketch: Rich and
/// Quick diverge on SQL shape (row insert vs function call), on parameter
/// shape (scalars vs arrays), and on read-back semantics (no read-back vs
/// per-batch version+sequence assignment). Trying to unify those at one
/// abstraction level just pushes the divergence into per-call branches.
/// Splitting at the storage level keeps each hot path branch-free.
/// </para>
/// </remarks>
public abstract class EventStorage<TId>
{
    /// <summary>
    /// Produces the operation(s) that append a stream's events. Rich
    /// returns N operations (one per event); Quick variants return 1
    /// (a batched call). Caller doesn't have to know which mode is in
    /// play — the session just enqueues whatever this yields.
    /// </summary>
    public abstract IEnumerable<IStorageOperation> AppendStreamEvents(StreamAction stream);

    /// <summary>
    /// Inserts the <c>mt_streams</c> row when a new stream is opened.
    /// Same shape in all three modes; default implementation reads its
    /// SQL from the descriptor.
    /// </summary>
    public abstract IStorageOperation InsertStream(StreamAction stream);

    /// <summary>
    /// Increments the <c>mt_streams</c> version with the expected-version
    /// guard. Same shape in all three modes.
    /// </summary>
    public abstract IStorageOperation UpdateStreamVersion(StreamAction stream);

    /// <summary>
    /// Stream-state lookup. Same shape in all three modes — included on
    /// the storage class rather than a separate hierarchy because the
    /// existing <c>EventDocumentStorage</c> co-locates it.
    /// </summary>
    public abstract IQueryHandler<StreamState?> StreamStateQueryHandler(TId streamId);
}
