#nullable enable
using System;
using JasperFx.Events;
using Marten.EventStorage.Querying;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq.QueryHandlers;

namespace Marten.EventStorage.QuickWithServerTimestamps;

/// <summary>
/// <see cref="EventStorage{TId}"/> for
/// <c>EventAppendMode.QuickWithServerTimestamps</c>. Same shape as
/// <see cref="Quick.QuickEventStorage{TId}"/> + the server-side <c>now()</c>
/// timestamp array; the returned timestamps get written back onto each
/// event in the operation's <c>Postprocess</c>.
/// </summary>
internal sealed class QuickWithServerTimestampsEventStorage<TId>: EventStorage<TId>
{
    private readonly QuickWithServerTimestampsEventStorageDescriptor _descriptor;

    public QuickWithServerTimestampsEventStorage(QuickWithServerTimestampsEventStorageDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public override IStorageOperation AppendEvent(IMartenSession session, StreamAction stream, IEvent @event)
        // Full-mode per-event INSERT for the tombstone / direct-AppendEvent
        // code paths that bypass the bulk function call.
        => new Quick.QuickAppendEventWithVersionOperation(
            _descriptor.AppendEventSqlPrefix,
            _descriptor.AppendEventFullSqlSuffix,
            _descriptor.AppendEventFullMetadataBinders,
            _descriptor.IsGuidStreamIdentity,
            _descriptor.SerializeEventData,
            _descriptor.SerializeEventBdata,
            stream,
            @event);

    public override IStorageOperation QuickAppendEventWithVersion(StreamAction stream, IEvent @event)
        => new Quick.QuickAppendEventWithVersionOperation(
            _descriptor.AppendEventSqlPrefix,
            _descriptor.AppendEventSqlSuffix,
            _descriptor.MetadataBinders,
            _descriptor.IsGuidStreamIdentity,
            _descriptor.SerializeEventData,
            _descriptor.SerializeEventBdata,
            stream,
            @event);

    public override IStorageOperation QuickAppendEvents(StreamAction stream)
        => new QuickAppendEventsWithServerTimestampsOperation(_descriptor, stream);

    public override IStorageOperation InsertStream(StreamAction stream)
        => new QuickWithServerTimestampsInsertStreamOperation(_descriptor, stream);

    public override IStorageOperation UpdateStreamVersion(StreamAction stream)
        => new QuickWithServerTimestampsUpdateStreamVersionOperation(_descriptor, stream);

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
