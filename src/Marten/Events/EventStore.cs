using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Schema.Identity;
using Marten.Storage;

namespace Marten.Events
{
    public class EventStore: IEventStore
    {
        private readonly DocumentSessionBase _session;
        private readonly ITenant _tenant;
        private readonly IEventSelector _selector;
        private readonly DocumentStore _store;

        public EventStore(DocumentSessionBase session, DocumentStore store, ITenant tenant)
        {
            _session = session;
            _store = store;

            _tenant = tenant;

            // TODO -- we can make much more of this lazy
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
            if (StreamIdentity == StreamIdentity.AsGuid)
                throw new InvalidOperationException("This Marten event store is configured to identify streams with Guids");
            _tenant.EnsureStorageExists(typeof(EventStream));
        }

        private void ensureAsGuidStorage()
        {
            if (StreamIdentity == StreamIdentity.AsString)
                throw new InvalidOperationException("This Marten event store is configured to identify streams with strings");
            _tenant.EnsureStorageExists(typeof(EventStream));
        }

        public StreamIdentity StreamIdentity { get; }

        public EventStream Append(Guid stream, IEnumerable<object> events)
        {
            return Append(stream, events?.ToArray());
        }

        public EventStream Append(Guid stream, params object[] events)
        {
            ensureAsGuidStorage();


            if (_session.UnitOfWork.TryFindStream(stream, out var eventStream))
            {
                eventStream.AddEvents(events.Select(EventStream.ToEvent));
            }
            else
            {
                eventStream = new EventStream(stream, events.Select(EventStream.ToEvent).ToArray(), false);
                var operation = new AppendEventsOperation(eventStream, _store.Events);
                _session.UnitOfWork.Add(operation);
            }

            return eventStream;
        }

        public EventStream Append(string stream, IEnumerable<object> events)
        {
            return Append(stream, events?.ToArray());
        }

        public EventStream Append(string stream, params object[] events)
        {
            ensureAsStringStorage();

            if (_session.UnitOfWork.TryFindStream(stream, out var eventStream))
            {
                eventStream.AddEvents(events.Select(EventStream.ToEvent));
            }
            else
            {
                eventStream = new EventStream(stream, events.Select(EventStream.ToEvent).ToArray(), false);
                var operation = new AppendEventsOperation(eventStream, _store.Events);
                _session.UnitOfWork.Add(operation);
            }

            return eventStream;
        }

        public EventStream Append(Guid stream, int expectedVersion, IEnumerable<object> events)
        {
            return Append(stream, expectedVersion, events?.ToArray());
        }

        public EventStream Append(Guid stream, int expectedVersion, params object[] events)
        {
            var eventStream = Append(stream, events);
            eventStream.ExpectedVersionOnServer = expectedVersion;

            return eventStream;
        }

        public EventStream Append(string stream, int expectedVersion, IEnumerable<object> events)
        {
            return Append(stream, expectedVersion, events?.ToArray());
        }

        public EventStream Append(string stream, int expectedVersion, params object[] events)
        {
            var eventStream = Append(stream, events);
            eventStream.ExpectedVersionOnServer = expectedVersion;

            return eventStream;
        }

        public EventStream StartStream<TAggregate>(Guid id, IEnumerable<object> events) where TAggregate : class
        {
            return StartStream<TAggregate>(id, events?.ToArray());
        }

        public EventStream StartStream<T>(Guid id, params object[] events) where T : class
        {
            return StartStream(typeof(T), id, events);
        }

        public EventStream StartStream(Type aggregateType, Guid id, IEnumerable<object> events)
        {
            return StartStream(aggregateType, id, events?.ToArray());
        }

        public EventStream StartStream(Type aggregateType, Guid id, params object[] events)
        {
            ensureAsGuidStorage();

            var stream = new EventStream(id, events.Select(EventStream.ToEvent).ToArray(), true)
            {
                AggregateType = aggregateType
            };

            var operation = new AppendEventsOperation(stream, _store.Events);
            _session.UnitOfWork.Add(operation);

            return stream;
        }

        public EventStream StartStream<TAggregate>(string streamKey, IEnumerable<object> events) where TAggregate : class
        {
            return StartStream<TAggregate>(streamKey, events?.ToArray());
        }

        public EventStream StartStream<TAggregate>(string streamKey, params object[] events) where TAggregate : class
        {
            return StartStream(typeof(TAggregate), streamKey, events);
        }

        public EventStream StartStream(Type aggregateType, string streamKey, IEnumerable<object> events)
        {
            return StartStream(aggregateType, streamKey, events?.ToArray());
        }

        public EventStream StartStream(Type aggregateType, string streamKey, params object[] events)
        {
            ensureAsStringStorage();

            var stream = new EventStream(streamKey, events.Select(EventStream.ToEvent).ToArray(), true)
            {
                AggregateType = aggregateType
            };

            var operation = new AppendEventsOperation(stream, _store.Events);
            _session.UnitOfWork.Add(operation);

            return stream;
        }

        public EventStream StartStream(Guid id, IEnumerable<object> events)
        {
            return StartStream(id, events?.ToArray());
        }

        public EventStream StartStream(Guid id, params object[] events)
        {
            ensureAsGuidStorage();

            var stream = new EventStream(id, events.Select(EventStream.ToEvent).ToArray(), true);

            var operation = new AppendEventsOperation(stream, _store.Events);
            _session.UnitOfWork.Add(operation);

            return stream;
        }

        public EventStream StartStream(string streamKey, IEnumerable<object> events)
        {
            return StartStream(streamKey, events?.ToArray());
        }

        public EventStream StartStream(string streamKey, params object[] events)
        {
            ensureAsStringStorage();

            var stream = new EventStream(streamKey, events.Select(EventStream.ToEvent).ToArray(), true);

            var operation = new AppendEventsOperation(stream, _store.Events);
            _session.UnitOfWork.Add(operation);

            return stream;
        }

        public EventStream StartStream<TAggregate>(IEnumerable<object> events) where TAggregate : class
        {
            return StartStream<TAggregate>(events?.ToArray());
        }

        public EventStream StartStream<TAggregate>(params object[] events) where TAggregate : class
        {
            return StartStream(typeof(TAggregate), events);
        }

        public EventStream StartStream(Type aggregateType, IEnumerable<object> events)
        {
            return StartStream(aggregateType, events?.ToArray());
        }

        public EventStream StartStream(Type aggregateType, params object[] events)
        {
            return StartStream(aggregateType, CombGuidIdGeneration.NewGuid(), events);
        }

        public EventStream StartStream(IEnumerable<object> events)
        {
            return StartStream(events?.ToArray());
        }

        public EventStream StartStream(params object[] events)
        {
            return StartStream(CombGuidIdGeneration.NewGuid(), events);
        }

        public IReadOnlyList<IEvent> FetchStream(Guid streamId, int version = 0, DateTime? timestamp = null)
        {
            ensureAsGuidStorage();

            var handler = new EventQueryHandler<Guid>(_selector, streamId, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
            return _session.ExecuteHandler(handler);
        }

        public Task<IReadOnlyList<IEvent>> FetchStreamAsync(Guid streamId, int version = 0, DateTime? timestamp = null, CancellationToken token = default(CancellationToken))
        {
            ensureAsGuidStorage();

            var handler = new EventQueryHandler<Guid>(_selector, streamId, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
            return _session.ExecuteHandlerAsync(handler, token);
        }

        public IReadOnlyList<IEvent> FetchStream(string streamKey, int version = 0, DateTime? timestamp = null)
        {
            ensureAsStringStorage();

            var handler = new EventQueryHandler<string>(_selector, streamKey, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
            return _session.ExecuteHandler(handler);
        }

        public Task<IReadOnlyList<IEvent>> FetchStreamAsync(string streamKey, int version = 0, DateTime? timestamp = null, CancellationToken token = default(CancellationToken))
        {
            ensureAsStringStorage();

            var handler = new EventQueryHandler<string>(_selector, streamKey, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
            return _session.ExecuteHandlerAsync(handler, token);
        }

        public T AggregateStream<T>(Guid streamId, int version = 0, DateTime? timestamp = null, T state = null) where T : class
        {
            ensureAsGuidStorage();

            var inner = new EventQueryHandler<Guid>(_selector, streamId, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
            var aggregator = _store.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner, _session, state);

            var aggregate = _session.ExecuteHandler(handler);

            var assignment = _tenant.IdAssignmentFor<T>();
            assignment.Assign(_tenant, aggregate, streamId);

            return aggregate;
        }

        public async Task<T> AggregateStreamAsync<T>(Guid streamId, int version = 0, DateTime? timestamp = null,
            T state = null, CancellationToken token = new CancellationToken()) where T : class
        {
            ensureAsGuidStorage();

            var inner = new EventQueryHandler<Guid>(_selector, streamId, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
            var aggregator = _store.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner, _session, state);

            var aggregate = await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);

            var assignment = _tenant.IdAssignmentFor<T>();
            assignment.Assign(_tenant, aggregate, streamId);

            return aggregate;
        }

        public T AggregateStream<T>(string streamKey, int version = 0, DateTime? timestamp = null, T state = null) where T : class
        {
            ensureAsStringStorage();

            var inner = new EventQueryHandler<string>(_selector, streamKey, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
            var aggregator = _store.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner, _session, state);

            var aggregate = _session.ExecuteHandler(handler);

            var assignment = _tenant.IdAssignmentFor<T>();
            assignment.Assign(_tenant, aggregate, streamKey);

            return aggregate;
        }

        public async Task<T> AggregateStreamAsync<T>(string streamKey, int version = 0, DateTime? timestamp = null,
            T state = null, CancellationToken token = new CancellationToken()) where T : class
        {
            ensureAsStringStorage();

            var inner = new EventQueryHandler<string>(_selector, streamKey, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
            var aggregator = _store.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner, _session, state);

            var aggregate = await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);

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

            _store.Events.AddEventType(typeof(T));

            return Load(id).As<Event<T>>();
        }

        public async Task<Event<T>> LoadAsync<T>(Guid id, CancellationToken token = default(CancellationToken)) where T : class
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            _store.Events.AddEventType(typeof(T));

            return (await LoadAsync(id, token).ConfigureAwait(false)).As<Event<T>>();
        }

        public IEvent Load(Guid id)
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new SingleEventQueryHandler(id, _store.Events, _store.Serializer);
            return _session.ExecuteHandler(handler);
        }

        public Task<IEvent> LoadAsync(Guid id, CancellationToken token = default(CancellationToken))
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new SingleEventQueryHandler(id, _store.Events, _store.Serializer);
            return _session.ExecuteHandlerAsync(handler, token);
        }

        public StreamState FetchStreamState(Guid streamId)
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new StreamStateByGuidHandler(_store.Events, streamId, _tenant.TenantId);
            return _session.ExecuteHandler(handler);
        }

        public Task<StreamState> FetchStreamStateAsync(Guid streamId, CancellationToken token = new CancellationToken())
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new StreamStateByGuidHandler(_store.Events, streamId, _tenant.TenantId);
            return _session.ExecuteHandlerAsync(handler, token);
        }

        public StreamState FetchStreamState(string streamKey)
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new StreamStateByStringHandler(_store.Events, streamKey, _tenant.TenantId);
            return _session.ExecuteHandler(handler);
        }

        public Task<StreamState> FetchStreamStateAsync(string streamKey, CancellationToken token = new CancellationToken())
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new StreamStateByStringHandler(_store.Events, streamKey, _tenant.TenantId);
            return _session.ExecuteHandlerAsync(handler, token);
        }
    }
}
