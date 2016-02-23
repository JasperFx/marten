using System;
using System.Collections.Generic;
using System.Data;
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
            events.Each(@event =>
            {
                var mapping = _schema.Events.EventMappingFor((Type) @event.GetType());

                appendEvent(mapping, stream, @event, null);
            });
        }

        private void appendEvent(EventMapping eventMapping, Guid stream, IEvent @event, string streamType)
        {
            if (@event.Id == Guid.Empty) @event.Id = Guid.NewGuid();

            _connection.Execute(cmd =>
            {
                cmd.CallsSproc("mt_append_event")
                    .With("stream", stream)
                    .With("stream_type", streamType)
                    .With("event_id", @event.Id)
                    .With("event_type", eventMapping.EventTypeName)
                    .With("body", _serializer.ToJson(@event), NpgsqlDbType.Jsonb)
                    .ExecuteNonQuery();
            });

                
        }

        public Guid StartStream<T>(params IEvent[] events) where T : IAggregate
        {
            // TODO --- temp!
            var streamStorage = _schema.StorageFor(typeof (Stream<T>)) as IAggregateStorage;
            var stream = Guid.NewGuid();

            events.Each(@event =>
            {
                var mapping = _schema.Events.EventMappingFor(@event.GetType());

                appendEvent(mapping, stream, @event, streamStorage.StreamTypeName);
            });

            return stream;
        }

        public T FetchSnapshot<T>(Guid streamId) where T : IAggregate
        {
            throw new NotImplementedException();
        }

        public IEnumerable<IEvent> FetchStream<T>(Guid streamId) where T : IAggregate
        {
            return _connection.Execute(cmd =>
            {
                using (var reader = cmd
                    .WithText("select type, data from mt_events where stream_id = :id order by version")
                    .With("id", streamId).ExecuteReader())
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
                return cmd.CallsSproc("mt_apply_transform")
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
            var projectionName = _schema.Events.StreamMappingFor<TAggregate>().StreamTypeName;

            var eventType = _schema.Events.EventMappingFor(@event.GetType()).EventTypeName;

            string json = _connection.Execute(cmd =>
            {
                return cmd.CallsSproc("mt_apply_aggregation")
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
            var projectionName = _schema.Events.StreamMappingFor<TAggregate>().StreamTypeName;

            var eventType = _schema.Events.EventMappingFor(@event.GetType()).EventTypeName;

            var json = _connection.Execute(cmd =>
            {
                return cmd.CallsSproc("mt_start_aggregation")
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