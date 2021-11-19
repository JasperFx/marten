using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events.Querying;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Storage;

#nullable enable
namespace Marten.Events
{
    internal class QueryEventStore: IQueryEventStore
    {
        private readonly QuerySession _session;
        private readonly ITenant _tenant;
        private readonly DocumentStore _store;

        public QueryEventStore(QuerySession session, DocumentStore store, ITenant tenant)
        {
            _session = session;
            _store = store;
            _tenant = tenant;
        }

        public IReadOnlyList<IEvent> FetchStream(Guid streamId, long version = 0, DateTimeOffset? timestamp = null, long fromVersion = 0)
        {
            var selector = _store.Events.EnsureAsGuidStorage(_session);

            var statement = new EventStatement(selector)
            {
                StreamId = streamId, Version = version, Timestamp = timestamp, TenantId = _tenant.TenantId, FromVersion = fromVersion
            };

            IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

            return _session.ExecuteHandler(handler);
        }

        public Task<IReadOnlyList<IEvent>> FetchStreamAsync(Guid streamId, long version = 0, DateTimeOffset? timestamp = null, long fromVersion = 0, CancellationToken token = default)
        {
            var selector = _store.Events.EnsureAsGuidStorage(_session);

            var statement = new EventStatement(selector)
            {
                StreamId = streamId, Version = version, Timestamp = timestamp, TenantId = _tenant.TenantId, FromVersion = fromVersion
            };

            IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

            return _session.ExecuteHandlerAsync(handler, token);
        }

        public IReadOnlyList<IEvent> FetchStream(string streamKey, long version = 0, DateTimeOffset? timestamp = null, long fromVersion = 0)
        {
            var selector = _store.Events.EnsureAsStringStorage(_session);

            var statement = new EventStatement(selector)
            {
                StreamKey = streamKey, Version = version, Timestamp = timestamp, TenantId = _tenant.TenantId, FromVersion = fromVersion
            };

            IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

            return _session.ExecuteHandler(handler);
        }

        public Task<IReadOnlyList<IEvent>> FetchStreamAsync(string streamKey, long version = 0, DateTimeOffset? timestamp = null, long fromVersion = 0, CancellationToken token = default)
        {
            var selector = _store.Events.EnsureAsStringStorage(_session);

            var statement = new EventStatement(selector)
            {
                StreamKey = streamKey, Version = version, Timestamp = timestamp, TenantId = _tenant.TenantId, FromVersion = fromVersion
            };

            IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

            return _session.ExecuteHandlerAsync(handler, token);
        }

        public T? AggregateStream<T>(Guid streamId, long version = 0, DateTimeOffset? timestamp = null, T? state = null, long fromVersion = 0) where T : class
        {
            var events = FetchStream(streamId, version, timestamp, fromVersion);

            var aggregator = _store.Options.Projections.AggregatorFor<T>();

            if (!events.Any()) return null;

            var aggregate = aggregator.Build(events, _session, state);

            var storage = _session.StorageFor<T>();
            if (storage is IDocumentStorage<T, Guid> s) s.SetIdentity(aggregate, streamId);

            return aggregate;
        }

        public async Task<T?> AggregateStreamAsync<T>(Guid streamId, long version = 0, DateTimeOffset? timestamp = null,
            T? state = null, long fromVersion = 0, CancellationToken token = default) where T : class
        {
            var events = await FetchStreamAsync(streamId, version, timestamp, fromVersion, token).ConfigureAwait(false);
            if (!events.Any()) return null;

            var aggregator = _store.Options.Projections.AggregatorFor<T>();
            var aggregate = await aggregator.BuildAsync(events, _session, state, token).ConfigureAwait(false);

            if (aggregate == null) return null;

            var storage = _session.StorageFor<T>();
            if (storage is IDocumentStorage<T, Guid> s) s.SetIdentity(aggregate, streamId);

            return aggregate;
        }

        public T? AggregateStream<T>(string streamKey, long version = 0, DateTimeOffset? timestamp = null, T? state = null, long fromVersion = 0) where T : class
        {
            var events = FetchStream(streamKey, version, timestamp, fromVersion);
            if (!events.Any())
            {
                return null;
            }

            var aggregator = _store.Options.Projections.AggregatorFor<T>();
            var aggregate = aggregator.Build(events, _session, state);

            var storage = _session.StorageFor<T>();
            if (storage is IDocumentStorage<T, string> s) s.SetIdentity(aggregate, streamKey);

            return aggregate;
        }

        public async Task<T?> AggregateStreamAsync<T>(string streamKey, long version = 0, DateTimeOffset? timestamp = null,
            T? state = null, long fromVersion = 0, CancellationToken token = default) where T : class
        {
            var events = await FetchStreamAsync(streamKey, version, timestamp, fromVersion, token).ConfigureAwait(false);
            if (!events.Any())
            {
                return null;
            }

            var aggregator = _store.Options.Projections.AggregatorFor<T>();

            var aggregate = await aggregator.BuildAsync(events, _session, state, token).ConfigureAwait(false);

            var storage = _session.StorageFor<T>();
            if (storage is IDocumentStorage<T, string> s) s.SetIdentity(aggregate, streamKey);

            return aggregate;
        }

        public IMartenQueryable<T> QueryRawEventDataOnly<T>()
        {
            _tenant.EnsureStorageExists(typeof(StreamAction));

            _store.Events.AddEventType(typeof(T));

            return _session.Query<T>();
        }

        public IMartenQueryable<IEvent> QueryAllRawEvents()
        {
            _tenant.EnsureStorageExists(typeof(StreamAction));

            return _session.Query<IEvent>();
        }

        public IEvent<T> Load<T>(Guid id) where T : class
        {
            _tenant.EnsureStorageExists(typeof(StreamAction));

            _store.Events.AddEventType(typeof(T));

            return Load(id).As<Event<T>>();
        }

        public async Task<IEvent<T>> LoadAsync<T>(Guid id, CancellationToken token = default) where T : class
        {
            await _tenant.EnsureStorageExistsAsync(typeof(StreamAction), token).ConfigureAwait(false);

            _store.Events.AddEventType(typeof(T));

            return (await LoadAsync(id, token).ConfigureAwait(false)).As<Event<T>>();
        }

        public IEvent Load(Guid id)
        {
            var handler = new SingleEventQueryHandler(id, _session.EventStorage());
            return _session.ExecuteHandler(handler);
        }

        public Task<IEvent> LoadAsync(Guid id, CancellationToken token = default)
        {
            _tenant.EnsureStorageExists(typeof(StreamAction));

            var handler = new SingleEventQueryHandler(id, _session.EventStorage());
            return _session.ExecuteHandlerAsync(handler, token);
        }

        public StreamState FetchStreamState(Guid streamId)
        {
            var handler = _tenant.EventStorage().QueryForStream(StreamAction.ForReference(streamId, _tenant));
            return _session.ExecuteHandler(handler);
        }

        public Task<StreamState> FetchStreamStateAsync(Guid streamId, CancellationToken token = default)
        {
            var handler = _tenant.EventStorage().QueryForStream(StreamAction.ForReference(streamId, _tenant));
            return _session.ExecuteHandlerAsync(handler, token);
        }

        public StreamState FetchStreamState(string streamKey)
        {
            var handler = _tenant.EventStorage().QueryForStream(StreamAction.ForReference(streamKey, _tenant));
            return _session.ExecuteHandler(handler);
        }

        public Task<StreamState> FetchStreamStateAsync(string streamKey, CancellationToken token = default)
        {
            var handler = _tenant.EventStorage().QueryForStream(StreamAction.ForReference(streamKey, _tenant));
            return _session.ExecuteHandlerAsync(handler, token);
        }
    }
}
