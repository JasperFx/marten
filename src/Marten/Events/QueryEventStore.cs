#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Events.Querying;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Storage;

namespace Marten.Events;

internal class QueryEventStore: IQueryEventStore, IReadOnlyEventStore
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

        var statement = new EventStatement(selector, _store.Events)
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

        var statement = new EventStatement(selector, _store.Events)
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

        if (version != 0 && version > events[events.Count - 1].Version) return null;

        var aggregator = _store.Options.Projections.AggregatorFor<T>();
        var aggregate = await aggregator.BuildAsync(events, _session, state, token).ConfigureAwait(false);

        if (aggregate == null)
        {
            return null;
        }

        if (_session.TryGetStorageForLiveAggregation<T>(out var storage))
        {
            storage.SetIdentityFromGuid(aggregate, streamId);
        }

        return aggregate;
    }

    public async Task<T?> AggregateStreamToLastKnownAsync<T>(Guid streamId, long version = 0,
        DateTimeOffset? timestamp = null,
        CancellationToken token = default) where T : class
    {
        var events = await FetchStreamAsync(streamId, version, timestamp, 0, token).ConfigureAwait(false);
        if (!events.Any())
        {
            return null;
        }

        var aggregator = _store.Options.Projections.AggregatorFor<T>();

        T? aggregate = null;
        while (aggregate == null && events.Any())
        {
            aggregate = await aggregator.BuildAsync(events, _session, default, token).ConfigureAwait(false);
            events = events.SkipLast(1).ToList();
        }

        if (aggregate != null)
        {
            if (_session.TryGetStorageForLiveAggregation<T>(out var storage))
            {
                storage!.SetIdentityFromGuid(aggregate, streamId);
            }
        }

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

        if (version != 0 && version > events[events.Count - 1].Version) return null;

        var aggregator = _store.Options.Projections.AggregatorFor<T>();

        var aggregate = await aggregator.BuildAsync(events, _session, state, token).ConfigureAwait(false);

        if (aggregate != null)
        {
            if (_session.TryGetStorageForLiveAggregation<T>(out var storage))
            {
                storage.SetIdentityFromString(aggregate, streamKey);
            }
        }

        return aggregate;
    }

    public async Task<T?> AggregateStreamToLastKnownAsync<T>(string streamKey, long version = 0, DateTimeOffset? timestamp = null,
        CancellationToken token = default) where T : class
    {
        var events = await FetchStreamAsync(streamKey, version, timestamp, 0, token).ConfigureAwait(false);
        if (!events.Any())
        {
            return null;
        }

        var aggregator = _store.Options.Projections.AggregatorFor<T>();

        T? aggregate = null;
        while (aggregate == null && events.Any())
        {
            aggregate = await aggregator.BuildAsync(events, _session, default, token).ConfigureAwait(false);
            events = events.SkipLast(1).ToList();
        }

        if (aggregate != null)
        {
            if (_session.TryGetStorageForLiveAggregation<T>(out var storage))
            {
                storage!.SetIdentityFromString(aggregate, streamKey);
            }
        }

        return aggregate;
    }

    public IMartenQueryable<T> QueryRawEventDataOnly<T>() where T : notnull
    {
        _store.Events.AddEventType<T>();

        return _session.Query<T>();
    }

    public IMartenQueryable<IEvent> QueryAllRawEvents()
    {
        return _session.Query<IEvent>();
    }

    public async Task<IEvent<T>?> LoadAsync<T>(Guid id, CancellationToken token = default) where T : class
    {
        await _tenant.Database.EnsureStorageExistsAsync(typeof(StreamAction), token).ConfigureAwait(false);

        _store.Events.AddEventType<T>();

        return (await LoadAsync(id, token).ConfigureAwait(false))?.As<Event<T>>();
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

    // IReadOnlyEventStore explicit implementations
    async Task<JasperFx.Events.StreamState?> IReadOnlyEventStore.FetchStreamStateAsync(Guid streamId, CancellationToken token)
    {
        var state = await FetchStreamStateAsync(streamId, token).ConfigureAwait(false);
        return state != null ? ToJasperFxStreamState(state) : null;
    }

    async Task<JasperFx.Events.StreamState?> IReadOnlyEventStore.FetchStreamStateAsync(string streamKey, CancellationToken token)
    {
        var state = await FetchStreamStateAsync(streamKey, token).ConfigureAwait(false);
        return state != null ? ToJasperFxStreamState(state) : null;
    }

    public async Task<PagedEvents> QueryEventsAsync(EventQuery query, CancellationToken token = default)
    {
        await _tenant.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var queryable = QueryAllRawEvents();

        if (query.EventTypeName != null)
        {
            queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.EventTypeName == query.EventTypeName);
        }

        if (query.StreamId != null)
        {
            if (Guid.TryParse(query.StreamId, out var streamGuid))
            {
                queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.StreamId == streamGuid);
            }
            else
            {
                queryable = (IMartenQueryable<IEvent>)queryable.Where(e => e.StreamKey == query.StreamId);
            }
        }

        var totalCount = await queryable.CountAsync(token).ConfigureAwait(false);

        var pageNumber = query.PageNumber <= 0 ? 1 : query.PageNumber;
        var offset = (pageNumber - 1) * query.PageSize;

        var events = await queryable
            .OrderByDescending(e => e.Sequence)
            .Skip(offset)
            .Take(query.PageSize)
            .ToListAsync(token)
            .ConfigureAwait(false);

        return new PagedEvents
        {
            Events = events,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = query.PageSize
        };
    }

    private static JasperFx.Events.StreamState ToJasperFxStreamState(StreamState martenState)
    {
        return new JasperFx.Events.StreamState
        {
            Id = martenState.Id,
            Key = martenState.Key,
            Version = martenState.Version,
            AggregateType = martenState.AggregateType,
            LastTimestamp = martenState.LastTimestamp,
            Created = martenState.Created,
            IsArchived = martenState.IsArchived
        };
    }
}
