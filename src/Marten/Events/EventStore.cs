using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;

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

            _selector = new EventSelector(_schema.Events.As<EventGraph>(), serializer);

        }

        public void Append(Guid stream, params object[] events)
        {


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

        public Guid StartStream<T>(Guid id, params object[] events) where T : class, new()
        {
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
            var handler = new EventQueryHandler(_selector, streamId, version, timestamp);
            return _connection.Fetch(handler, null);
        }

        public Task<IList<IEvent>> FetchStreamAsync(Guid streamId, int version = 0, DateTime? timestamp = null,
            CancellationToken token = new CancellationToken())
        {
            var handler = new EventQueryHandler(_selector, streamId, version, timestamp);
            return _connection.FetchAsync(handler, null, token);
        }

        public T AggregateStream<T>(Guid streamId, int version = 0, DateTime? timestamp = null) where T : class, new()
        {
            var inner = new EventQueryHandler(_selector, streamId, version, timestamp);
            var aggregator = _schema.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner);

            return _connection.Fetch(handler, null);
        }

        public Task<T> AggregateStreamAsync<T>(Guid streamId, int version = 0, DateTime? timestamp = null,
            CancellationToken token = new CancellationToken()) where T : class, new()
        {
            var inner = new EventQueryHandler(_selector, streamId, version, timestamp);
            var aggregator = _schema.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner);

            return _connection.FetchAsync(handler, null, token);
        }


        public IMartenQueryable<T> Query<T>()
        {
            _schema.Events.AddEventType(typeof (T));

            return _session.Query<T>();
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
            var handler = new SingleEventQueryHandler(id, _schema.Events.As<EventGraph>(), _serializer);
            return _connection.Fetch(handler, new NulloIdentityMap(_serializer));
        }

        public Task<IEvent> LoadAsync(Guid id, CancellationToken token = default(CancellationToken))
        {
            var handler = new SingleEventQueryHandler(id, _schema.Events.As<EventGraph>(), _serializer);
            return _connection.FetchAsync(handler, new NulloIdentityMap(_serializer), token);
        }

        public StreamState FetchStreamState(Guid streamId)
        {
            var handler = new StreamStateHandler(_schema.Events.As<EventGraph>(), streamId);
            return _connection.Fetch(handler, null);
        }

        public Task<StreamState> FetchStreamStateAsync(Guid streamId, CancellationToken token = new CancellationToken())
        {
            var handler = new StreamStateHandler(_schema.Events.As<EventGraph>(), streamId);
            return _connection.FetchAsync(handler, null, token);
        }
    }
}