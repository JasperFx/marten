using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Events;
using Marten.Storage;

namespace Marten.Events
{
    public class EventStore : IEventStore
    {
        private readonly IDocumentSession _session;
        private readonly IManagedConnection _connection;
        private readonly UnitOfWork _unitOfWork;
        private readonly ITenant _tenant;
        private readonly EventSelector _selector;
        private readonly DocumentStore _store;


        public EventStore(IDocumentSession session, DocumentStore store, IManagedConnection connection, UnitOfWork unitOfWork, ITenant tenant)
        {
            _session = session;
            _store = store;
            
            _connection = connection;
            _unitOfWork = unitOfWork;
            _tenant = tenant;

            _selector = new EventSelector(_store.Events, _store.Serializer);

        }

        public void Append(Guid stream, params object[] events)
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            if (_unitOfWork.HasStream(stream))
            {
                _unitOfWork.StreamFor(stream).AddEvents(events.Select(EventStream.ToEvent));
            }
            else
            {
                var eventStream = new EventStream(stream, events.Select(EventStream.ToEvent).ToArray(), false);

                _unitOfWork.StoreStream(eventStream);
            }
        }

        public void Append(Guid stream, int expectedVersion, params object[] events)
        {
            Append(stream, events);

            var assertion =
                _unitOfWork.NonDocumentOperationsOf<AssertEventStreamMaxEventId>()
                    .FirstOrDefault(x => x.Stream == stream);

            if (assertion == null)
            {
                _unitOfWork.Add(new AssertEventStreamMaxEventId(stream, expectedVersion, _store.Events.Table.QualifiedName));
            }
            else
            {
                assertion.ExpectedVersion = expectedVersion;
            }

            
        }

        public Guid StartStream<T>(Guid id, params object[] events) where T : class, new()
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var stream = new EventStream(id, events.Select(EventStream.ToEvent).ToArray(), true)
            {
                AggregateType = typeof (T)
            };

            _unitOfWork.StoreStream(stream);

            return id;
        }

        public Guid StartStream<TAggregate>(params object[] events) where TAggregate : class, new()
        {
            return StartStream<TAggregate>(Guid.NewGuid(), events);
        }

        public IList<IEvent> FetchStream(Guid streamId, int version = 0, DateTime? timestamp = null)
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new EventQueryHandler(_selector, streamId, version, timestamp);
            return _connection.Fetch(handler, null, null);
        }

        public Task<IList<IEvent>> FetchStreamAsync(Guid streamId, int version = 0, DateTime? timestamp = null,
            CancellationToken token = new CancellationToken())
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new EventQueryHandler(_selector, streamId, version, timestamp);
            return _connection.FetchAsync(handler, null, null, token);
        }

        public T AggregateStream<T>(Guid streamId, int version = 0, DateTime? timestamp = null) where T : class, new()
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var inner = new EventQueryHandler(_selector, streamId, version, timestamp);
            var aggregator = _store.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner, _session);

            var aggregate = _connection.Fetch(handler, null, null);

            var assignment = _tenant.IdAssignmentFor<T>();
            assignment.Assign(aggregate, streamId);


            return aggregate;
        }

        public async Task<T> AggregateStreamAsync<T>(Guid streamId, int version = 0, DateTime? timestamp = null,
            CancellationToken token = new CancellationToken()) where T : class, new()
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var inner = new EventQueryHandler(_selector, streamId, version, timestamp);
            var aggregator = _store.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner, _session);

            var aggregate = await _connection.FetchAsync(handler, null, null, token).ConfigureAwait(false);

            var assignment = _tenant.IdAssignmentFor<T>();
            assignment.Assign(aggregate, streamId);


            return aggregate;
        }


        public IMartenQueryable<T> QueryRawEventDataOnly<T>()
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            if (_store.Events.AllAggregates().Any(x => x.AggregateType == typeof(T)))
            {
                return _session.Query<T>();
            }

            if (_tenant.AllMappings.All(x => x.DocumentType != typeof(T)))
            {
                _store.Events.AddEventType(typeof(T));
            }
            

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
            return _connection.Fetch(handler, new NulloIdentityMap(_store.Serializer), null);
        }

        public Task<IEvent> LoadAsync(Guid id, CancellationToken token = default(CancellationToken))
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new SingleEventQueryHandler(id, _store.Events, _store.Serializer);
            return _connection.FetchAsync(handler, new NulloIdentityMap(_store.Serializer), null, token);
        }

        public StreamState FetchStreamState(Guid streamId)
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new StreamStateHandler(_store.Events, streamId);
            return _connection.Fetch(handler, null, null);
        }

        public Task<StreamState> FetchStreamStateAsync(Guid streamId, CancellationToken token = new CancellationToken())
        {
            _tenant.EnsureStorageExists(typeof(EventStream));

            var handler = new StreamStateHandler(_store.Events, streamId);
            return _connection.FetchAsync(handler, null, null, token);
        }
    }
}