using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
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

        private FunctionName ApplyTransformFunction => new FunctionName(_schema.Events.DatabaseSchemaName, "mt_apply_transform");
        private FunctionName ApplyAggregationFunction => new FunctionName(_schema.Events.DatabaseSchemaName, "mt_apply_aggregation");
        private FunctionName StartAggregationFunction => new FunctionName(_schema.Events.DatabaseSchemaName, "mt_start_aggregation");

        public EventStore(IDocumentSession session, IIdentityMap identityMap, IDocumentSchema schema, ISerializer serializer, IManagedConnection connection)
        {
            _session = session;
            _identityMap = identityMap;
            _schema = schema;
            _serializer = serializer;
            _connection = connection;
        }

        public void Append(Guid stream, object @event)
        {
            throw new NotImplementedException();
        }

        public void AppendEvents(Guid stream, params object[] events)
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
                AggregateType = typeof(T)
            };

            _session.Store(stream);

            return id;
        }

        public Guid StartStream<TAggregate>(params object[] events) where TAggregate : class, new()
        {
            return StartStream<TAggregate>(Guid.NewGuid(), events);
        }

        public TAggregate FetchSnapshot<TAggregate>(Guid streamId) where TAggregate : class, new()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<IEvent> FetchStream(Guid streamId)
        {
            return _connection.Execute(cmd =>
            {
                using (var reader = cmd
                    .WithText($"select id, type, version, data from {_schema.Events.DatabaseSchemaName}.mt_events where stream_id = :stream_id order by version")
                    .With("stream_id", streamId).ExecuteReader())
                {
                    return fetchStream(reader).ToArray();
                }
            });
        }

        public IEnumerable<IEvent> FetchStream(Guid streamId, int version)
        {
            return _connection.Execute(cmd =>
            {
                using (var reader = cmd
                    .WithText($"select id, type, version, data from {_schema.Events.DatabaseSchemaName}.mt_events where stream_id = :stream_id and version <= :version order by version")
                    .With("stream_id", streamId).With("version", version).ExecuteReader())
                {
                    return fetchStream(reader).ToArray();
                }
            });
        }
        public IEnumerable<IEvent> FetchStream(Guid streamId, DateTime timestamp)
        {
            if (timestamp.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentOutOfRangeException(nameof(timestamp), "This method only accepts UTC dates");
            }

            return _connection.Execute(cmd =>
            {
                using (var reader = cmd
                    .WithText($"select id, type, version, data from {_schema.Events.DatabaseSchemaName}.mt_events where stream_id = :stream_id and timestamp <= :timestamp order by version")
                    .With("stream_id", streamId).With("timestamp", timestamp).ExecuteReader())
                {
                    return fetchStream(reader).ToArray();
                }
            });
        }
        private IEnumerable<IEvent> fetchStream(IDataReader reader)
        {
            // TODO -- turn this into an ISelector to standardize
            while (reader.Read())
            {
                var id = reader.GetGuid(0);
                var eventTypeName = reader.GetString(1);
                var version = reader.GetInt32(2);
                var dataJson = reader.GetString(3);

                var mapping = _schema.Events.EventMappingFor(eventTypeName);

                var data = _serializer.FromJson(mapping.DocumentType, dataJson).As<object>();


                var @event = EventStream.ToEvent(data);
                @event.Version = version;
                @event.Id = id;


                yield return @event;
            }
        }

        public ITransforms Transforms => this;
 

        public IMartenQueryable<T> Query<T>()
        {
            _schema.Events.AddEventType(typeof(T));

            return _session.Query<T>();
        }

        public T Load<T>(Guid id) where T : class
        {
            _schema.Events.AddEventType(typeof(T));
            return _session.Load<T>(id);
        }

        public Task<T> LoadAsync<T>(Guid id) where T : class
        {
            _schema.Events.AddEventType(typeof(T));
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

        public TAggregate ApplySnapshot<TAggregate>(Guid streamId, TAggregate aggregate, IEvent @event) where TAggregate : class, new()
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
            return _connection.Execute(cmd =>
            {
                Func<DbDataReader, StreamState> converter = r =>
                {
                    var typeName = r.GetString(1);
                    var aggregateType = _schema.Events.AggregateTypeFor(typeName);
                    return new StreamState(streamId, r.GetInt32(0), aggregateType);
                };

                return cmd.Fetch($"select version, type from {_schema.Events.DatabaseSchemaName}.mt_streams where id = ?", converter, streamId).SingleOrDefault();
            });
        }
    }
}