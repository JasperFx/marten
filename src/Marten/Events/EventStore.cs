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

        public void Append(Guid stream, object @event)
        {
            throw new NotImplementedException();
        }

        public void AppendEvents(Guid stream, params object[] events)
        {
            if (_identityMap.Has<EventStream>(stream))
            {
                _identityMap.Retrieve<EventStream>(stream).AddEvents(events.Select(x => new Event { Body = x }));
            }
            else
            {
                var eventStream = new EventStream(stream, events.Select(x => new Event { Body = x }).ToArray());

                _session.Store(eventStream);
            }
        }

        public Guid StartStream<T>(Guid id, params object[] events) where T : class, new()
        {
            var stream = new EventStream(id, events.Select(x => new Event { Body = x }).ToArray())
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

        public IEnumerable<object> FetchStream(Guid streamId)
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

        public IEnumerable<object> FetchStream(Guid streamId, int version)
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
        public IEnumerable<object> FetchStream(Guid streamId, DateTime timestamp)
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
        private IEnumerable<object> fetchStream(IDataReader reader)
        {
            while (reader.Read())
            {
                var eventTypeName = reader.GetString(0);
                var json = reader.GetString(1);

                var mapping = _schema.Events.EventMappingFor(eventTypeName);

                yield return _serializer.FromJson(mapping.DocumentType, json).As<object>();
            }
        }

        public void DeleteEvent(Guid id)
        {
            throw new NotImplementedException();
        }

        public void DeleteEvent(Event @event)
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

        public TAggregate TransformTo<TAggregate>(Guid streamId, Event @event)
        {
            throw new NotImplementedException();
        }

        public string Transform(string projectionName, Guid streamId, Event @event)
        {
            var mapping = _schema.Events.EventMappingFor(@event.Body.GetType());
            var eventType = mapping.EventTypeName;

            var eventJson = _serializer.ToJson(@event.Body);

            var json = _connection.Execute(cmd =>
            {
                return cmd.CallsSproc(_schema.Events.DatabaseSchemaName + ".mt_apply_transform")
                    .With("stream_id", streamId)
                    .With("event_id", @event.Id)
                    .With("projection", projectionName)
                    .With("event_type", eventType)
                    .With("event", eventJson, NpgsqlDbType.Json).ExecuteScalar();
            });

            return json.ToString();
        }

        public TAggregate ApplySnapshot<TAggregate>(Guid streamId, TAggregate aggregate, Event @event) where TAggregate : class, new()
        {
            var aggregateJson = _serializer.ToJson(aggregate);
            var projectionName = _schema.Events.AggregateFor<TAggregate>().Alias;

            var eventType = _schema.Events.EventMappingFor(@event.Body.GetType()).EventTypeName;

            string json = _connection.Execute(cmd =>
            {
                return cmd.CallsSproc(_schema.Events.DatabaseSchemaName + ".mt_apply_aggregation")
                    .With("stream_id", streamId)
                    .With("event_id", @event.Id)
                    .With("projection", projectionName)
                    .With("event_type", eventType)
                    .With("event", _serializer.ToJson(@event.Body), NpgsqlDbType.Json)
                    .With("aggregate", aggregateJson, NpgsqlDbType.Json).ExecuteScalar().As<string>();
            });

            return _serializer.FromJson<TAggregate>(json);
        }

        public TAggregate ApplyProjection<TAggregate>(string projectionName, TAggregate aggregate, Event @event) where TAggregate : class, new()
        {
            throw new NotImplementedException();
        }

        public TAggregate StartSnapshot<TAggregate>(Guid streamId, Event @event) where TAggregate : class, new()
        {
            var projectionName = _schema.Events.AggregateFor<TAggregate>().Alias;

            var eventType = _schema.Events.EventMappingFor(@event.Body.GetType()).EventTypeName;

            var json = _connection.Execute(cmd =>
            {
                return cmd.CallsSproc(_schema.Events.DatabaseSchemaName + ".mt_start_aggregation")
                    .With("stream_id", streamId)
                    .With("event_id", @event.Id)
                    .With("projection", projectionName)
                    .With("event_type", eventType)
                    .With("event", _serializer.ToJson(@event.Body), NpgsqlDbType.Json)
                    .ExecuteScalar().As<string>();
            });

            return _serializer.FromJson<TAggregate>(json);
        }
    }
}