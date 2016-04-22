using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Events
{
    public class EventStore : IEventStore, ITransforms
    {
        private readonly IDocumentSession _session;
        private readonly IIdentityMap _identityMap;
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;
        private readonly IManagedConnection _connection;
        private readonly EventSelector _selector;

        private FunctionName ApplyTransformFunction
            => new FunctionName(_schema.Events.DatabaseSchemaName, "mt_apply_transform");

        private FunctionName ApplyAggregationFunction
            => new FunctionName(_schema.Events.DatabaseSchemaName, "mt_apply_aggregation");

        private FunctionName StartAggregationFunction
            => new FunctionName(_schema.Events.DatabaseSchemaName, "mt_start_aggregation");

        public EventStore(IDocumentSession session, IIdentityMap identityMap, IDocumentSchema schema,
            ISerializer serializer, IManagedConnection connection)
        {
            _session = session;
            _identityMap = identityMap;
            _schema = schema;
            _serializer = serializer;
            _connection = connection;

            _selector = new EventSelector(_schema.Events.As<EventGraph>(), _serializer);
        }

        public void Append(Guid stream, params object[] events)
        {
            if (_identityMap.Has<EventStream>(stream))
            {
                _identityMap.Retrieve<EventStream>(stream).AddEvents(events.Select(EventStream.ToEvent));
            }
            else
            {
                var eventStream = new EventStream(stream, events.Select(EventStream.ToEvent).ToArray(), false);

                _session.Store(eventStream);
            }
        }

        public Guid StartStream<T>(Guid id, params object[] events) where T : class, new()
        {
            var stream = new EventStream(id, events.Select(EventStream.ToEvent).ToArray(), true)
            {
                AggregateType = typeof (T)
            };

            _session.Store(stream);

            return id;
        }

        public Guid StartStream<TAggregate>(params object[] events) where TAggregate : class, new()
        {
            return StartStream<TAggregate>(Guid.NewGuid(), events);
        }

        public IEnumerable<IEvent> FetchStream(Guid streamId, int version = 0, DateTime? timestamp = null)
        {
            var handler = new EventQueryHandler(_selector, streamId, version, timestamp);
            return _connection.Fetch(handler, null);
        }

        public T AggregateStream<T>(Guid streamId, int version = 0, DateTime? timestamp = null) where T : class, new()
        {
            var inner = new EventQueryHandler(_selector, streamId, version, timestamp);
            var aggregator = _schema.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner);

            return _connection.Fetch(handler, null);
        }


        public ITransforms Transforms => this;


        public IMartenQueryable<T> Query<T>()
        {
            _schema.Events.AddEventType(typeof (T));

            return _session.Query<T>();
        }

        public T Load<T>(Guid id) where T : class
        {
            _schema.Events.AddEventType(typeof (T));
            return _session.Load<T>(id);
        }

        public Task<T> LoadAsync<T>(Guid id) where T : class
        {
            _schema.Events.AddEventType(typeof (T));
            return _session.LoadAsync<T>(id);
        }


        public string Transform(string projectionName, Guid streamId, IEvent @event)
        {
            var mapping = _schema.Events.EventMappingFor(@event.Data.GetType());
            var eventType = mapping.EventTypeName;

            var eventJson = _serializer.ToJson(@event.Data);

            var json = _connection.Execute(cmd =>
            {
                return cmd.CallsSproc(ApplyTransformFunction)
                    .With("stream_id", streamId)
                    .With("event_id", @event.Id)
                    .With("projection", projectionName)
                    .With("event_type", eventType)
                    .With("event", eventJson, NpgsqlDbType.Json).ExecuteScalar();
            });

            return json.ToString();
        }

        public TAggregate ApplySnapshot<TAggregate>(Guid streamId, TAggregate aggregate, IEvent @event)
            where TAggregate : class, new()
        {
            var aggregateJson = _serializer.ToJson(aggregate);
            var projectionName = _schema.Events.AggregateFor<TAggregate>().Alias;

            var eventType = _schema.Events.EventMappingFor(@event.Data.GetType()).EventTypeName;

            string json = _connection.Execute(cmd =>
            {
                return cmd.CallsSproc(ApplyAggregationFunction)
                    .With("stream_id", streamId)
                    .With("event_id", @event.Id)
                    .With("projection", projectionName)
                    .With("event_type", eventType)
                    .With("event", _serializer.ToJson(@event.Data), NpgsqlDbType.Json)
                    .With("aggregate", aggregateJson, NpgsqlDbType.Json).ExecuteScalar().As<string>();
            });

            return _serializer.FromJson<TAggregate>(json);
        }

        public TAggregate StartSnapshot<TAggregate>(Guid streamId, IEvent @event) where TAggregate : class, new()
        {
            var projectionName = _schema.Events.AggregateFor<TAggregate>().Alias;

            var eventType = _schema.Events.EventMappingFor(@event.Data.GetType()).EventTypeName;

            var json = _connection.Execute(cmd =>
            {
                return cmd.CallsSproc(StartAggregationFunction)
                    .With("stream_id", streamId)
                    .With("event_id", @event.Id)
                    .With("projection", projectionName)
                    .With("event_type", eventType)
                    .With("event", _serializer.ToJson(@event.Data), NpgsqlDbType.Json)
                    .ExecuteScalar().As<string>();
            });

            return _serializer.FromJson<TAggregate>(json);
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