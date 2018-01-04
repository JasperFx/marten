using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Services;
using Marten.Storage;

namespace Marten.Events
{
    public class EventStore : IEventStore
    {
        private readonly IDocumentSession _session;
        private readonly IManagedConnection _connection;
        private readonly UnitOfWork _unitOfWork;
        private readonly ITenant _tenant;
        private readonly ISelector<IEvent> _selector;
        private readonly DocumentStore _store;


        public EventStore(IDocumentSession session, DocumentStore store, IManagedConnection connection, UnitOfWork unitOfWork, ITenant tenant)
        {
            _session = session;
            _store = store;
            
            _connection = connection;
            _unitOfWork = unitOfWork;
            _tenant = tenant;

            StreamIdentity = _store.Events.StreamIdentity;

            if (StreamIdentity == StreamIdentity.AsGuid)
            {
                _selector = new EventSelector(_store.Events, _store.Serializer);
            }
            else
            {
                _selector = new StringIdentifiedEventSelector(_store.Events, _store.Serializer);
            }

        }

        private void ensureAsStringStorage()
        {
            if (StreamIdentity == StreamIdentity.AsGuid) throw new InvalidOperationException("This Marten event store is configured to identify streams with Guids");
            _tenant.EnsureStorageExists(typeof(EventStream));
        }

        private void ensureAsGuidStorage()
        {
            if (StreamIdentity == StreamIdentity.AsString) throw new InvalidOperationException("This Marten event store is configured to identify streams with strings");
            _tenant.EnsureStorageExists(typeof(EventStream));
        }

        public StreamIdentity StreamIdentity { get; }

        public EventStream Append(Guid stream, params object[] events)
        {
            ensureAsGuidStorage();

            EventStream eventStream = null;

            if (_unitOfWork.HasStream(stream))
            {
                eventStream = _unitOfWork.StreamFor(stream);
                eventStream.AddEvents(events.Select(EventStream.ToEvent));
            }
            else
            {
                eventStream = new EventStream(stream, events.Select(EventStream.ToEvent).ToArray(), false);
                _unitOfWork.StoreStream(eventStream);
            }

            return eventStream;
        }

        public EventStream Append(string stream, params object[] events)
        {
            ensureAsStringStorage();

            EventStream eventStream = null;

            if (_unitOfWork.HasStream(stream))
            {
                eventStream = _unitOfWork.StreamFor(stream);
                eventStream.AddEvents(events.Select(EventStream.ToEvent));
            }
            else
            {
                eventStream = new EventStream(stream, events.Select(EventStream.ToEvent).ToArray(), false);
                _unitOfWork.StoreStream(eventStream);
            }

            return eventStream;
        }

        public EventStream Append(Guid stream, int expectedVersion, params object[] events)
        {
            var eventStream = Append(stream, events);
            eventStream.ExpectedVersionOnServer = expectedVersion;

            return eventStream;
        }

        public EventStream Append(string stream, int expectedVersion, params object[] events)
        {
            var eventStream = Append(stream, events);
            eventStream.ExpectedVersionOnServer = expectedVersion;

            return eventStream;
        }

        public EventStream StartStream<T>(Guid id, params object[] events) where T : class, new()
        {
            ensureAsGuidStorage();

            var stream = new EventStream(id, events.Select(EventStream.ToEvent).ToArray(), true)
            {
                AggregateType = typeof (T)
            };

            _unitOfWork.StoreStream(stream);

            return stream;
        }

        public EventStream StartStream<TAggregate>(string streamKey, params object[] events) where TAggregate : class, new()
        {
            ensureAsStringStorage();

            var stream = new EventStream(streamKey, events.Select(EventStream.ToEvent).ToArray(), true)
            {
                AggregateType = typeof(TAggregate)
            };

            _unitOfWork.StoreStream(stream);

            return stream;
        }

        public EventStream StartStream(Guid id, params object[] events)
        {
            ensureAsGuidStorage();

            var stream = new EventStream(id, events.Select(EventStream.ToEvent).ToArray(), true);

            _unitOfWork.StoreStream(stream);

            return stream;
        }

        public EventStream StartStream(string streamKey, params object[] events)
        {
            ensureAsStringStorage();

            var stream = new EventStream(streamKey, events.Select(EventStream.ToEvent).ToArray(), true);

            _unitOfWork.StoreStream(stream);

            return stream;
        }

        public EventStream StartStream<TAggregate>(params object[] events) where TAggregate : class, new()
        {
            return StartStream<TAggregate>(Guid.NewGuid(), events);
        }

        public EventStream StartStream(params object[] events)
        {
            return StartStream(Guid.NewGuid(), events);
        }

        public IReadOnlyList<IEvent> FetchStream(Guid streamId, int version = 0, DateTime? timestamp = null)
        {
            ensureAsGuidStorage();

            var handler = new EventQueryHandler<Guid>(_selector, streamId, version, timestamp);
            return _connection.Fetch(handler, null, null, _tenant);
        }

        public Task<IReadOnlyList<IEvent>> FetchStreamAsync(Guid streamId, int version = 0, DateTime? timestamp = null, CancellationToken token = default(CancellationToken))
        {
            ensureAsGuidStorage();

            var handler = new EventQueryHandler<Guid>(_selector, streamId, version, timestamp);
            return _connection.FetchAsync(handler, null, null, _tenant, token);
        }

        public IReadOnlyList<IEvent> FetchStream(string streamKey, int version = 0, DateTime? timestamp = null)
        {
            ensureAsStringStorage();

            var handler = new EventQueryHandler<string>(_selector, streamKey, version, timestamp);
            return _connection.Fetch(handler, null, null, _tenant);
        }

        public Task<IReadOnlyList<IEvent>> FetchStreamAsync(string streamKey, int version = 0, DateTime? timestamp = null, CancellationToken token = default(CancellationToken))
        {
            ensureAsStringStorage();

            var handler = new EventQueryHandler<string>(_selector, streamKey, version, timestamp);
            return _connection.FetchAsync(handler, null, null, _tenant, token);
        }

        public T AggregateStream<T>(Guid streamId, int version = 0, DateTime? timestamp = null, T state = null) where T : class, new()
        {
            ensureAsGuidStorage();

            var inner = new EventQueryHandler<Guid>(_selector, streamId, version, timestamp);
            var aggregator = _store.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner, _session, state);

            var aggregate = _connection.Fetch(handler, null, null, _tenant);

            var assignment = _tenant.IdAssignmentFor<T>();
            assignment.Assign(_tenant, aggregate, streamId);


            return aggregate;
        }

        public async Task<T> AggregateStreamAsync<T>(Guid streamId, int version = 0, DateTime? timestamp = null,
            T state = null, CancellationToken token = new CancellationToken()) where T : class, new()
        {
            ensureAsGuidStorage();

            var inner = new EventQueryHandler<Guid>(_selector, streamId, version, timestamp);
            var aggregator = _store.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner, _session, state);

            var aggregate = await _connection.FetchAsync(handler, null, null, _tenant, token).ConfigureAwait(false);

            var assignment = _tenant.IdAssignmentFor<T>();
            assignment.Assign(_tenant, aggregate, streamId);


            return aggregate;
        }

        public T AggregateStream<T>(string streamKey, int version = 0, DateTime? timestamp = null, T state = null) where T : class, new()
        {
            ensureAsStringStorage();

            var inner = new EventQueryHandler<string>(_selector, streamKey, version, timestamp);
            var aggregator = _store.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner, _session, state);

            var aggregate = _connection.Fetch(handler, null, null, _tenant);

            var assignment = _tenant.IdAssignmentFor<T>();
            assignment.Assign(_tenant, aggregate, streamKey);


            return aggregate;
        }

        public async Task<T> AggregateStreamAsync<T>(string streamKey, int version = 0, DateTime? timestamp = null,
            T state = null, CancellationToken token = new CancellationToken()) where T : class, new()
        {
            ensureAsStringStorage();

            var inner = new EventQueryHandler<string>(_selector, streamKey, version, timestamp);
            var aggregator = _store.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner, _session, state);

            var aggregate = await _connection.FetchAsync(handler, null, null, _tenant, token).ConfigureAwait(false);

            var assignment = _tenant.IdAssignmentFor<T>();
            assignment.Assign(_tenant, aggregate, streamKey);


            return aggregate;
        }


        public IMartenQueryable<T> QueryRawEventDataOnly<T>()
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            if (_store.Events.AllAggregates().Any(x => x.AggregateType == typeof(T)))
            {
                return _session.Query<T>();
            }

            _store.Events.AddEventType(typeof(T));


            return _session.Query<T>();
        }

        public IMartenQueryable<IEvent> QueryAllRawEvents()
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            return _session.Query<IEvent>();
        }

        public Event<T> Load<T>(Guid id) where T : class
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            _store.Events.AddEventType(typeof (T));


            return Load(id).As<Event<T>>();
        }

        public async Task<Event<T>> LoadAsync<T>(Guid id, CancellationToken token = default(CancellationToken)) where T : class
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            _store.Events.AddEventType(typeof (T));

            return (await LoadAsync(id, token).ConfigureAwait(false)).As<Event<T>>();
        }

        public IEvent Load(Guid id)
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new SingleEventQueryHandler(id, _store.Events, _store.Serializer);
            return _connection.Fetch(handler, new NulloIdentityMap(_store.Serializer), null, _tenant);
        }

        public Task<IEvent> LoadAsync(Guid id, CancellationToken token = default(CancellationToken))
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new SingleEventQueryHandler(id, _store.Events, _store.Serializer);
            return _connection.FetchAsync(handler, new NulloIdentityMap(_store.Serializer), null, _tenant, token);
        }

        public StreamState FetchStreamState(Guid streamId)
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new StreamStateByGuidHandler(_store.Events, streamId);
            return _connection.Fetch(handler, null, null, _tenant);
        }

        public Task<StreamState> FetchStreamStateAsync(Guid streamId, CancellationToken token = new CancellationToken())
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new StreamStateByGuidHandler(_store.Events, streamId);
            return _connection.FetchAsync(handler, null, null, _tenant, token);
        }

        public StreamState FetchStreamState(string streamKey)
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new StreamStateByStringHandler(_store.Events, streamKey);
            return _connection.Fetch(handler, null, null, _tenant);
        }

        public Task<StreamState> FetchStreamStateAsync(string streamKey, CancellationToken token = new CancellationToken())
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new StreamStateByStringHandler(_store.Events, streamKey);
            return _connection.FetchAsync(handler, null, null, _tenant, token);
        }
    }
}