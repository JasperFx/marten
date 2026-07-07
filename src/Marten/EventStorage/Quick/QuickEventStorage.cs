#nullable enable
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;

namespace Marten.EventStorage.Quick;

/// <summary>
/// <see cref="EventStorage{TId}"/> for <c>EventAppendMode.Quick</c> — batch
/// append via the <c>mt_quick_append_events</c> server function with array
/// parameters covering every event in the stream. RETURNING-array
/// read-back assigns server-generated versions + sequences onto the
/// events list.
/// </summary>
internal sealed class QuickEventStorage<TId>: EventStorage<TId>
{
    private readonly QuickEventStorageDescriptor _descriptor;

    public QuickEventStorage(QuickEventStorageDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public override IStorageOperation AppendEvent(IStorageSession session, StreamAction stream, IEvent @event)
        // Full-mode per-event INSERT — seq_id is a bound parameter. The
        // tombstone batch (and a few other code paths) call AppendEvent
        // directly regardless of AppendMode, with sequences pre-assigned
        // on @event.Sequence.
        => new QuickAppendEventWithVersionOperation(
            _descriptor.AppendEventSqlPrefix,
            _descriptor.AppendEventFullSqlSuffix,
            _descriptor.AppendEventFullMetadataBinders,
            _descriptor.IsGuidStreamIdentity,
            _descriptor.SerializeEventData,
            _descriptor.SerializeEventBdata,
            stream,
            @event);

    public override IStorageOperation QuickAppendEventWithVersion(StreamAction stream, IEvent @event)
        => new QuickAppendEventWithVersionOperation(
            _descriptor.AppendEventSqlPrefix,
            _descriptor.AppendEventSqlSuffix,
            _descriptor.MetadataBinders,
            _descriptor.IsGuidStreamIdentity,
            _descriptor.SerializeEventData,
            _descriptor.SerializeEventBdata,
            stream,
            @event);

    public override IStorageOperation QuickAppendEvents(StreamAction stream)
        // E0 prep for the Weasel.Storage move: the batched append operation is
        // dialect-supplied. Postgres binds array parameters to the
        // mt_quick_append_events function; another dialect may use an entirely
        // different SQL shape (e.g. multi-row INSERT + OUTPUT on SQL Server),
        // so the operation's construction lives on the descriptor, not here.
        => _descriptor.CreateQuickAppendEventsOperation(_descriptor, stream);

    public override IStorageOperation InsertStream(StreamAction stream)
        => new QuickInsertStreamOperation(_descriptor, stream);

    public override IStorageOperation UpdateStreamVersion(StreamAction stream)
        => new QuickUpdateStreamVersionOperation(_descriptor, stream);

    public override IStorageOperation AssertStreamVersion(StreamAction stream)
        => _descriptor.IsTenancyConjoined
            ? new ConjoinedAssertStreamVersionOperation<TId>(_descriptor.AssertStreamVersionSql, stream)
            : new SingleTenantAssertStreamVersionOperation<TId>(_descriptor.AssertStreamVersionSql, stream);
}
