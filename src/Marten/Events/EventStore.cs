using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Deletes;
using Marten.Services.Events;

namespace Marten.Events
{
    public class EventStore : IEventStore
    {
        private readonly IDocumentSession _session;
        private readonly IDocumentSchema _schema;
        private readonly IManagedConnection _connection;
        private readonly UnitOfWork _unitOfWork;
        private readonly EventSelector _selector;
        private readonly ISerializer _serializer;


        public EventStore(IDocumentSession session, IDocumentSchema schema, ISerializer serializer, IManagedConnection connection, UnitOfWork unitOfWork)
        {
            _session = session;
            _schema = schema;
            _serializer = serializer;
            
            _connection = connection;
            _unitOfWork = unitOfWork;

            _selector = new EventSelector(_schema.Events, serializer);

        }

        public void Append(Guid stream, params object[] events)
        {
            _schema.EnsureStorageExists(typeof(EventStream));

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
            _unitOfWork.Add(new AssertEventStreamMaxEventId(stream, expectedVersion, _schema.Events.Table.QualifiedName));
        }

        public Guid StartStream<T>(Guid id, params object[] events) where T : class, new()
        {
            _schema.EnsureStorageExists(typeof(EventStream));

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

        public IList<IEvent> FetchStream(Guid streamId, int version = 0, DateTime? timestamp = null, int? limit = null)
        {
            var handler = new EventQueryHandler(_selector, streamId, version, timestamp, limit);
            return _connection.Fetch(handler, null);
        }

        public Task<IList<IEvent>> FetchStreamAsync(Guid streamId, int version = 0, DateTime? timestamp = null, int? limit = null,
            CancellationToken token = new CancellationToken())
        {
            var handler = new EventQueryHandler(_selector, streamId, version, timestamp, limit);
            return _connection.FetchAsync(handler, null, token);
        }

        public T AggregateStream<T>(Guid streamId, int version = 0, DateTime? timestamp = null) where T : class, new()
        {
            var inner = new EventQueryHandler(_selector, streamId, version, timestamp);
            var aggregator = _schema.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner, _session);

            var aggregate = _connection.Fetch(handler, null);

            var assignment = _schema.IdAssignmentFor<T>();
            assignment.Assign(aggregate, streamId);


            return aggregate;
        }

        public async Task<T> AggregateStreamAsync<T>(Guid streamId, int version = 0, DateTime? timestamp = null,
            CancellationToken token = new CancellationToken()) where T : class, new()
        {
            var inner = new EventQueryHandler(_selector, streamId, version, timestamp);
            var aggregator = _schema.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner, _session);

            var aggregate = await _connection.FetchAsync(handler, null, token).ConfigureAwait(false);

            var assignment = _schema.IdAssignmentFor<T>();
            assignment.Assign(aggregate, streamId);


            return aggregate;
        }


        public IMartenQueryable<T> QueryRawEventDataOnly<T>()
        {
            if (_schema.Events.AllAggregates().Any(x => x.AggregateType == typeof(T)))
            {
                return _session.Query<T>();
            }

            if (_schema.AllMappings.All(x => x.DocumentType != typeof(T)))
            {
                _schema.Events.AddEventType(typeof(T));
            }
            

            return _session.Query<T>();
        }

        public IMartenQueryable<IEvent> QueryAllRawEvents()
        {
            return _session.Query<IEvent>();
        }

        public Event<T> Load<T>(Guid id) where T : class
        {
            _schema.Events.AddEventType(typeof (T));


            return Load(id).As<Event<T>>();
        }

        public async Task<Event<T>> LoadAsync<T>(Guid id, CancellationToken token = default(CancellationToken)) where T : class
        {
            _schema.Events.AddEventType(typeof (T));

            return (await LoadAsync(id, token).ConfigureAwait(false)).As<Event<T>>();
        }

        public IEvent Load(Guid id)
        {
            var handler = new SingleEventQueryHandler(id, _schema.Events, _serializer);
            return _connection.Fetch(handler, new NulloIdentityMap(_serializer));
        }

        public Task<IEvent> LoadAsync(Guid id, CancellationToken token = default(CancellationToken))
        {
            var handler = new SingleEventQueryHandler(id, _schema.Events, _serializer);
            return _connection.FetchAsync(handler, new NulloIdentityMap(_serializer), token);
        }

        public StreamState FetchStreamState(Guid streamId)
        {
            var handler = new StreamStateHandler(_schema.Events, streamId);
            return _connection.Fetch(handler, null);
        }

        public Task<StreamState> FetchStreamStateAsync(Guid streamId, CancellationToken token = new CancellationToken())
        {
            var handler = new StreamStateHandler(_schema.Events, streamId);
            return _connection.FetchAsync(handler, null, token);
        }
    }
}