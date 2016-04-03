using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using Baseline;
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

        public EventStore(IDocumentSession session, IIdentityMap identityMap, IDocumentSchema schema, ISerializer serializer, IManagedConnection connection)
        {
            _session = session;
            _identityMap = identityMap;
            _schema = schema;
            _serializer = serializer;
            _connection = connection;
        }

        public void Append<T>(Guid stream, T @event) where T : IEvent
        {
            throw new NotImplementedException();
        }

        public void AppendEvents(Guid stream, params IEvent[] events)
        {
            if (_identityMap.Has<EventStream>(stream))
            {
                _identityMap.Retrieve<EventStream>(stream).AddEvents(events);
            }
            else
            {
                var eventStream = new EventStream(stream, events);


                _session.Store(eventStream);
            }
        }

        public Guid StartStream<T>(Guid id, params IEvent[] events) where T : IAggregate
        {
            var stream = new EventStream(id, events)
            {
                AggregateType = typeof(T)
            };

            _session.Store(stream);

            return id;
        }

        public Guid StartStream<T>(params IEvent[] events) where T : IAggregate
        {
            return StartStream<T>(Guid.NewGuid(), events);
        }

        public T FetchSnapshot<T>(Guid streamId) where T : IAggregate
        {
            throw new NotImplementedException();
        }

        public IEnumerable<IEvent> FetchStream(Guid streamId) 
        {
            return _connection.Execute(cmd =>
            {
                using (var reader = cmd
                    .WithText($"select type, data from {_schema.Events.DatabaseSchemaName}.mt_events where stream_id = :id order by version")
                    .With("id", streamId).ExecuteReader())
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
                    .WithText($"select type, data from {_schema.Events.DatabaseSchemaName}.mt_events where stream_id = :id and version <= :version order by version")
                    .With("id", streamId).With("version", version).ExecuteReader())
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
                    .WithText($"select type, data from {_schema.Events.DatabaseSchemaName}.mt_events where stream_id = :id and timestamp <= :timestamp order by version")
                    .With("id", streamId).With("timestamp", timestamp).ExecuteReader())
                {
                    return fetchStream(reader).ToArray();
                }
            });
        }

        private IEnumerable<IEvent> fetchStream(IDataReader reader)
        {
            while (reader.Read())
            {
                var eventTypeName = reader.GetString(0);
                var json = reader.GetString(1);

                var mapping = _schema.Events.EventMappingFor(eventTypeName);

                yield return _serializer.FromJson(mapping.DocumentType, json).As<IEvent>();
            }
        }

        public void DeleteEvent<T>(Guid id)
        {
            throw new NotImplementedException();
        }

        public void DeleteEvent<T>(T @event) where T : IEvent
        {
            throw new NotImplementedException();
        }

        public void ReplaceEvent<T>(T @event)
        {
            throw new NotImplementedException();
        }

        public ITransforms Transforms => this;
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

        public TTarget TransformTo<TEvent, TTarget>(Guid stream, TEvent @event) where TEvent : IEvent
        {
            throw new NotImplementedException();
        }

        public string Transform(string projectionName, Guid stream, IEvent @event)
        {
            var mapping = _schema.Events.EventMappingFor(@event.GetType());
            var eventType = mapping.EventTypeName;

            var eventJson = _serializer.ToJson(@event);

            var json = _connection.Execute(cmd =>
            {
                return cmd.CallsSproc(_schema.Events.DatabaseSchemaName + ".mt_apply_transform")
                    .With("stream_id", stream)
                    .With("event_id", @event.Id)
                    .With("projection", projectionName)
                    .With("event_type", eventType)
                    .With("event", eventJson, NpgsqlDbType.Json).ExecuteScalar();
            });

            return json.ToString();
        }

        public TAggregate ApplySnapshot<TAggregate>(TAggregate aggregate, IEvent @event) where TAggregate : IAggregate
        {
 
            var aggregateId = aggregate.Id;
            var aggregateJson = _serializer.ToJson(aggregate);
            var projectionName = _schema.Events.AggregateFor<TAggregate>().Alias;

            var eventType = _schema.Events.EventMappingFor(@event.GetType()).EventTypeName;

            string json = _connection.Execute(cmd =>
            {
                return cmd.CallsSproc(_schema.Events.DatabaseSchemaName + ".mt_apply_aggregation")
                    .With("stream_id", aggregateId)
                    .With("event_id", @event.Id)
                    .With("projection", projectionName)
                    .With("event_type", eventType)
                    .With("event", _serializer.ToJson(@event), NpgsqlDbType.Json)
                    .With("aggregate", aggregateJson, NpgsqlDbType.Json).ExecuteScalar().As<string>();
            });

            var returnValue = _serializer.FromJson<TAggregate>(json);

            returnValue.Id = aggregateId;

            return returnValue;
            
        }

        public T ApplyProjection<T>(string projectionName, T aggregate, IEvent @event) where T : IAggregate
        {
            throw new NotImplementedException();
        }

        public TAggregate StartSnapshot<TAggregate>(IEvent @event) where TAggregate : IAggregate
        {
            var aggregateId = Guid.NewGuid();
            var projectionName = _schema.Events.AggregateFor<TAggregate>().Alias;

            var eventType = _schema.Events.EventMappingFor(@event.GetType()).EventTypeName;

            var json = _connection.Execute(cmd =>
            {
                return cmd.CallsSproc(_schema.Events.DatabaseSchemaName + ".mt_start_aggregation")
                    .With("stream_id", aggregateId)
                    .With("event_id", @event.Id)
                    .With("projection", projectionName)
                    .With("event_type", eventType)
                    .With("event", _serializer.ToJson(@event), NpgsqlDbType.Json)
                    .ExecuteScalar().As<string>();
            });

            var returnValue = _serializer.FromJson<TAggregate>(json);

            returnValue.Id = aggregateId;

            return returnValue;
            
        }
    }
}