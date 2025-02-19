#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Events.Querying;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Storage;

namespace Marten.Events;

internal class QueryEventStore: IQueryEventStore
{
    private readonly QuerySession _session;
    private readonly DocumentStore _store;
    protected readonly Tenant _tenant;

    public QueryEventStore(QuerySession session, DocumentStore store, Tenant tenant)
    {
        _session = session;
        _store = store;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<IEvent>> FetchStreamAsync(Guid streamId, long version = 0,
        DateTimeOffset? timestamp = null, long fromVersion = 0, CancellationToken token = default)
    {
        var selector = _store.Events.EnsureAsGuidStorage(_session);

        await _tenant.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var statement = new EventStatement(selector)
        {
            StreamId = streamId,
            Version = version,
            Timestamp = timestamp,
            TenantId = _tenant.TenantId,
            FromVersion = fromVersion
        };

        IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

        return await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<IEvent>> FetchStreamAsync(string streamKey, long version = 0,
        DateTimeOffset? timestamp = null, long fromVersion = 0, CancellationToken token = default)
    {
        var selector = _store.Events.EnsureAsStringStorage(_session);

        await _tenant.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var statement = new EventStatement(selector)
        {
            StreamKey = streamKey,
            Version = version,
            Timestamp = timestamp,
            TenantId = _tenant.TenantId,
            FromVersion = fromVersion
        };

        IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

        return await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public async Task<T?> AggregateStreamAsync<T>(Guid streamId, long version = 0, DateTimeOffset? timestamp = null,
        T? state = null, long fromVersion = 0, CancellationToken token = default) where T : class
    {
        var events = await FetchStreamAsync(streamId, version, timestamp, fromVersion, token).ConfigureAwait(false);
        if (!events.Any())
        {
            return state;
        }

        var aggregator = _store.Options.Projections.AggregatorFor<T>();
        var aggregate = await aggregator.BuildAsync(events, _session, state, token).ConfigureAwait(false);

        if (aggregate == null)
        {
            return null;
        }

        var storage = _session.StorageFor<T>();
        storage.SetIdentityFromGuid(aggregate, streamId);

        return aggregate;
    }

    public async Task<T?> AggregateStreamAsync<T>(string streamKey, long version = 0, DateTimeOffset? timestamp = null,
        T? state = null, long fromVersion = 0, CancellationToken token = default) where T : class
    {
        var events = await FetchStreamAsync(streamKey, version, timestamp, fromVersion, token).ConfigureAwait(false);
        if (!events.Any())
        {
            return state;
        }

        var aggregator = _store.Options.Projections.AggregatorFor<T>();

        var aggregate = await aggregator.BuildAsync(events, _session, state, token).ConfigureAwait(false);

        var storage = _session.StorageFor<T>();
        storage.SetIdentityFromString(aggregate, streamKey);

        return aggregate;
    }

    public IMartenQueryable<T> QueryRawEventDataOnly<T>()
    {
        _store.Events.AddEventType(typeof(T));

        return _session.Query<T>();
    }

    public IMartenQueryable<IEvent> QueryAllRawEvents()
    {
        return _session.Query<IEvent>();
    }

    public async Task<IEvent<T>?> LoadAsync<T>(Guid id, CancellationToken token = default) where T : class
    {
        await _tenant.Database.EnsureStorageExistsAsync(typeof(StreamAction), token).ConfigureAwait(false);

        _store.Events.AddEventType(typeof(T));

        return (await LoadAsync(id, token).ConfigureAwait(false)).As<Event<T>>();
    }

    public async Task<IEvent?> LoadAsync(Guid id, CancellationToken token = default)
    {
        await _tenant.Database.EnsureStorageExistsAsync(typeof(StreamAction), token).ConfigureAwait(false);

        var handler = new SingleEventQueryHandler(id, _session.EventStorage());
        return await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public async Task<StreamState?> FetchStreamStateAsync(Guid streamId, CancellationToken token = default)
    {
        await _tenant.Database.EnsureStorageExistsAsync(typeof(StreamAction), token).ConfigureAwait(false);
        var handler = eventStorage().QueryForStream(StreamAction.ForReference(streamId, _tenant.TenantId));
        return await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public async Task<StreamState?> FetchStreamStateAsync(string streamKey, CancellationToken token = default)
    {
        await _tenant.Database.EnsureStorageExistsAsync(typeof(StreamAction), token).ConfigureAwait(false);
        var handler = eventStorage().QueryForStream(StreamAction.ForReference(streamKey, _tenant.TenantId));
        return await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    private IEventStorage eventStorage()
    {
        return _store.Options.Providers.StorageFor<IEvent>().QueryOnly.As<IEventStorage>();
    }
}
