#nullable enable
using System;
using JasperFx.Events;
using Marten.EventStorage.Querying;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.QueryHandlers;

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

    public override IStorageOperation AppendEvent(IMartenSession session, StreamAction stream, IEvent @event)
        => throw new NotSupportedException(
            $"{nameof(QuickEventStorage<TId>)} batches events per stream — single-event appends route through " +
            $"{nameof(QuickAppendEvents)}.");

    public override IStorageOperation QuickAppendEventWithVersion(StreamAction stream, IEvent @event)
        => new QuickAppendEventWithVersionOperation(
            _descriptor.AppendEventSqlPrefix,
            _descriptor.AppendEventSqlSuffix,
            _descriptor.MetadataBinders,
            _descriptor.IsGuidStreamIdentity,
            _descriptor.SerializeEventData,
            stream,
            @event);

    public override IStorageOperation QuickAppendEvents(StreamAction stream)
        => new QuickAppendEventsOperation(_descriptor, stream);

    public override IStorageOperation InsertStream(StreamAction stream)
        => new QuickInsertStreamOperation(_descriptor, stream);

    public override IStorageOperation UpdateStreamVersion(StreamAction stream)
        => new QuickUpdateStreamVersionOperation(_descriptor, stream);

    public override IQueryHandler<StreamState> QueryForStream(StreamAction stream)
    {
        object streamIdentity = typeof(TId) == typeof(Guid)
            ? stream.Id
            : stream.Key!;

        var tenantId = _descriptor.IsTenancyConjoined ? stream.TenantId : null;

        return new ClosedShapeStreamStateQueryHandler<TId>(
            _descriptor.StreamStateSelectSql,
            (TId)streamIdentity,
            tenantId);
    }
}
