#nullable enable
using System;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.QueryHandlers;

namespace Marten.EventStorage;

/// <summary>
/// Closed-shape event-storage abstraction. Three concrete implementations
/// ship — one per append-mode flavor — and exactly one is wired into a
/// <see cref="DocumentStore"/> at construction time based on the
/// <c>StoreOptions.Events.AppendMode</c> setting. Per-call dispatch is a
/// virtual call through this base; no runtime branching on append mode.
/// </summary>
/// <remarks>
/// <para>
/// W4 (#4410). Method set mirrors <see cref="Marten.Events.EventDocumentStorage"/>
/// so the <c>ClosedShapeEventDocumentStorage</c> adapter can delegate
/// straight through without reshaping calls. Each concrete subclass
/// implements only the methods appropriate to its append mode:
/// </para>
/// <list type="bullet">
///   <item><see cref="Rich.RichEventStorage{TId}"/> — implements
///         <see cref="AppendEvent"/> + <see cref="QuickAppendEventWithVersion"/>.
///         <see cref="QuickAppendEvents"/> throws
///         <see cref="NotSupportedException"/>.</item>
///   <item><see cref="Quick.QuickEventStorage{TId}"/> — implements
///         <see cref="QuickAppendEvents"/>. <see cref="AppendEvent"/> +
///         <see cref="QuickAppendEventWithVersion"/> throw.</item>
///   <item><see cref="QuickWithServerTimestamps.QuickWithServerTimestampsEventStorage{TId}"/>
///         — same as Quick + the server-timestamp variant in its
///         <see cref="QuickAppendEvents"/> body.</item>
/// </list>
/// <para>
/// The throwing "wrong-mode" methods aren't a runtime hazard — the
/// adapter's pick of which subclass to instantiate is based on
/// <c>StoreOptions.Events.AppendMode</c>, and the session code is
/// gated by the same setting. The asymmetric API matches today's
/// <see cref="Marten.Events.EventDocumentStorage"/> shape.
/// </para>
/// </remarks>
public abstract class EventStorage<TId>
{
    /// <summary>
    /// Per-event append for the Full mode. One <see cref="IStorageOperation"/>
    /// per event; the session enqueues N operations for an N-event stream.
    /// Only <see cref="Rich.RichEventStorage{TId}"/> implements this.
    /// </summary>
    public abstract IStorageOperation AppendEvent(
        IMartenSession session, StreamAction stream, IEvent @event);

    /// <summary>
    /// Per-event append for the QuickWithVersion mode — same per-event shape
    /// as <see cref="AppendEvent"/>, but the event's version is pre-assigned
    /// by the caller rather than computed from a stream-state lookup. Only
    /// <see cref="Rich.RichEventStorage{TId}"/> implements this.
    /// </summary>
    public abstract IStorageOperation QuickAppendEventWithVersion(StreamAction stream, IEvent @event);

    /// <summary>
    /// Batched per-stream append. One <see cref="IStorageOperation"/> per
    /// stream; the operation's body iterates the stream's events list and
    /// binds them as <c>NpgsqlDbType.Array</c> parameters to the
    /// <c>mt_quick_append_events</c> server function. Implemented by
    /// <see cref="Quick.QuickEventStorage{TId}"/> and
    /// <see cref="QuickWithServerTimestamps.QuickWithServerTimestampsEventStorage{TId}"/>.
    /// </summary>
    public abstract IStorageOperation QuickAppendEvents(StreamAction stream);

    /// <summary>Inserts the <c>mt_streams</c> row when a new stream is opened.</summary>
    public abstract IStorageOperation InsertStream(StreamAction stream);

    /// <summary>Increments the <c>mt_streams</c> version with an expected-version guard.</summary>
    public abstract IStorageOperation UpdateStreamVersion(StreamAction stream);

    /// <summary>Stream-state lookup query handler.</summary>
    public abstract IQueryHandler<StreamState> QueryForStream(StreamAction stream);
}
