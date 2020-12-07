using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly DocumentStore _store;

        public EventStore(DocumentSessionBase session, DocumentStore store, ITenant tenant)
        {
            _session = session;
            _store = store;

            _tenant = tenant;
        }

        public StreamAction Append(Guid stream, IEnumerable<object> events)
        {
            return Append(stream, events?.ToArray());
        }

        public StreamAction Append(Guid stream, params object[] events)
        {
            return _store.Events.Append(_session, stream, events);
        }

        public StreamAction Append(string stream, IEnumerable<object> events)
        {
            return Append(stream, events?.ToArray());
        }

        public StreamAction Append(string stream, params object[] events)
        {
            return _store.Events.Append(_session, stream, events);
        }

        public StreamAction Append(Guid stream, int expectedVersion, IEnumerable<object> events)
        {
            return Append(stream, expectedVersion, events?.ToArray());
        }

        public StreamAction Append(Guid stream, int expectedVersion, params object[] events)
        {
            var eventStream = Append(stream, events);
            eventStream.ExpectedVersionOnServer = expectedVersion - eventStream.Events.Count;

            return eventStream;
        }

        public StreamAction Append(string stream, int expectedVersion, IEnumerable<object> events)
        {
            return Append(stream, expectedVersion, events?.ToArray());
        }

        public StreamAction Append(string stream, int expectedVersion, params object[] events)
        {
            var eventStream = Append(stream, events);
            eventStream.ExpectedVersionOnServer = expectedVersion - events.Length;

            return eventStream;
        }

        public StreamAction StartStream<TAggregate>(Guid id, IEnumerable<object> events) where TAggregate : class
        {
            return StartStream<TAggregate>(id, events?.ToArray());
        }

        public StreamAction StartStream<T>(Guid id, params object[] events) where T : class
        {
            return StartStream(typeof(T), id, events);
        }

        public StreamAction StartStream(Type aggregateType, Guid id, IEnumerable<object> events)
        {
            return StartStream(aggregateType, id, events?.ToArray());
        }

        public StreamAction StartStream(Type aggregateType, Guid id, params object[] events)
        {
            var stream = _store.Events.StartStream(_session, id, events);
            stream.AggregateType = aggregateType;

            return stream;
        }

        public StreamAction StartStream<TAggregate>(string streamKey, IEnumerable<object> events) where TAggregate : class
        {
            return StartStream<TAggregate>(streamKey, events?.ToArray());
        }

        public StreamAction StartStream<TAggregate>(string streamKey, params object[] events) where TAggregate : class
        {
            return StartStream(typeof(TAggregate), streamKey, events);
        }

        public StreamAction StartStream(Type aggregateType, string streamKey, IEnumerable<object> events)
        {
            return StartStream(aggregateType, streamKey, events?.ToArray());
        }

        public StreamAction StartStream(Type aggregateType, string streamKey, params object[] events)
        {
            var stream = _store.Events.StartStream(_session, streamKey, events);
            stream.AggregateType = aggregateType;

            return stream;
        }

        public StreamAction StartStream(Guid id, IEnumerable<object> events)
        {
            return StartStream(id, events?.ToArray());
        }

        public StreamAction StartStream(Guid id, params object[] events)
        {
            return _store.Events.StartStream(_session, id, events);
        }

        public StreamAction StartStream(string streamKey, IEnumerable<object> events)
        {
            return StartStream(streamKey, events?.ToArray());
        }

        public StreamAction StartStream(string streamKey, params object[] events)
        {
            return _store.Events.StartStream(_session, streamKey, events);
        }

        public StreamAction StartStream<TAggregate>(IEnumerable<object> events) where TAggregate : class
        {
            return StartStream<TAggregate>(events?.ToArray());
        }

        public StreamAction StartStream<TAggregate>(params object[] events) where TAggregate : class
        {
            return StartStream(typeof(TAggregate), events);
        }

        public StreamAction StartStream(Type aggregateType, IEnumerable<object> events)
        {
            return StartStream(aggregateType, events?.ToArray());
        }

        public StreamAction StartStream(Type aggregateType, params object[] events)
        {
            return StartStream(aggregateType, CombGuidIdGeneration.NewGuid(), events);
        }

        public StreamAction StartStream(IEnumerable<object> events)
        {
            return StartStream(events?.ToArray());
        }

        public StreamAction StartStream(params object[] events)
        {
            return StartStream(CombGuidIdGeneration.NewGuid(), events);
        }

        public IReadOnlyList<IEvent> FetchStream(Guid streamId, int version = 0, DateTime? timestamp = null)
        {
            // TODO -- do this later by just delegating to Load<StreamState>(streamId)
            var selector = _store.Events.EnsureAsGuidStorage(_session);

            var handler = new EventQueryHandler<Guid>(selector, streamId, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
            return _session.ExecuteHandler(handler);
        }

        public Task<IReadOnlyList<IEvent>> FetchStreamAsync(Guid streamId, int version = 0, DateTime? timestamp = null, CancellationToken token = default(CancellationToken))
        {
            var selector = _store.Events.EnsureAsGuidStorage(_session);

            var handler = new EventQueryHandler<Guid>(selector, streamId, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
            return _session.ExecuteHandlerAsync(handler, token);
        }

        public IReadOnlyList<IEvent> FetchStream(string streamKey, int version = 0, DateTime? timestamp = null)
        {
            var selector = _store.Events.EnsureAsStringStorage(_session);

            var handler = new EventQueryHandler<string>(selector, streamKey, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
            return _session.ExecuteHandler(handler);
        }

        public Task<IReadOnlyList<IEvent>> FetchStreamAsync(string streamKey, int version = 0, DateTime? timestamp = null, CancellationToken token = default(CancellationToken))
        {
            var selector = _store.Events.EnsureAsStringStorage(_session);

            var handler = new EventQueryHandler<string>(selector, streamKey, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
            return _session.ExecuteHandlerAsync(handler, token);
        }

        public T AggregateStream<T>(Guid streamId, int version = 0, DateTime? timestamp = null, T state = null) where T : class
        {
            var selector = _store.Events.EnsureAsGuidStorage(_session);

            var inner = new EventQueryHandler<Guid>(selector, streamId, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
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
            var selector = _store.Events.EnsureAsGuidStorage(_session);

            var inner = new EventQueryHandler<Guid>(selector, streamId, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
            var aggregator = _store.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner, _session, state);

            var aggregate = await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);

            var assignment = _tenant.IdAssignmentFor<T>();
            assignment.Assign(_tenant, aggregate, streamId);

            return aggregate;
        }

        public T AggregateStream<T>(string streamKey, int version = 0, DateTime? timestamp = null, T state = null) where T : class
        {
            var selector = _store.Events.EnsureAsStringStorage(_session);

            var inner = new EventQueryHandler<string>(selector, streamKey, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
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
            var selector = _store.Events.EnsureAsStringStorage(_session);

            var inner = new EventQueryHandler<string>(selector, streamKey, version, timestamp, _store.Events.TenancyStyle, _tenant.TenantId);
            var aggregator = _store.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner, _session, state);

            var aggregate = await _session.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);

            var assignment = _tenant.IdAssignmentFor<T>();
            assignment.Assign(_tenant, aggregate, streamKey);

            return aggregate;
        }

        public IMartenQueryable<T> QueryRawEventDataOnly<T>()
        {
            _tenant.EnsureStorageExists(typeof(StreamAction));

            if (_store.Events.AllAggregates().Any(x => x.AggregateType == typeof(T)))
            {
                return _session.Query<T>();
            }

            _store.Events.AddEventType(typeof(T));

            return _session.Query<T>();
        }

        public IMartenQueryable<IEvent> QueryAllRawEvents()
        {
            _tenant.EnsureStorageExists(typeof(StreamAction));

            return _session.Query<IEvent>();
        }

        public Event<T> Load<T>(Guid id) where T : class
        {
            _tenant.EnsureStorageExists(typeof(StreamAction));

            _store.Events.AddEventType(typeof(T));

            return Load(id).As<Event<T>>();
        }

        public async Task<Event<T>> LoadAsync<T>(Guid id, CancellationToken token = default(CancellationToken)) where T : class
        {
            _tenant.EnsureStorageExists(typeof(StreamAction));

            _store.Events.AddEventType(typeof(T));

            return (await LoadAsync(id, token).ConfigureAwait(false)).As<Event<T>>();
        }

        public IEvent Load(Guid id)
        {
            var handler = new SingleEventQueryHandler(id, _session.EventStorage());
            return _session.ExecuteHandler(handler);
        }

        public Task<IEvent> LoadAsync(Guid id, CancellationToken token = default(CancellationToken))
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

        public Task<StreamState> FetchStreamStateAsync(Guid streamId, CancellationToken token = new CancellationToken())
        {
            var handler = _tenant.EventStorage().QueryForStream(StreamAction.ForReference(streamId, _tenant));
            return _session.ExecuteHandlerAsync(handler, token);
        }

        public StreamState FetchStreamState(string streamKey)
        {
            var handler = _tenant.EventStorage().QueryForStream(StreamAction.ForReference(streamKey, _tenant));
            return _session.ExecuteHandler(handler);
        }

        public Task<StreamState> FetchStreamStateAsync(string streamKey, CancellationToken token = new CancellationToken())
        {
            var handler = _tenant.EventStorage().QueryForStream(StreamAction.ForReference(streamKey, _tenant));
            return _session.ExecuteHandlerAsync(handler, token);
        }
    }
}
