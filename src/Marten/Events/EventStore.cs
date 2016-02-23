using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events
{
    public class EventStore
    {
        private readonly IManagedConnection _connection;
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;
        private readonly IDocumentSchemaCreation _creation;
        private readonly FileSystem _files = new FileSystem();

        public EventStore(IManagedConnection connection, IDocumentSchema schema, ISerializer serializer, IDocumentSchemaCreation creation)
        {
            _connection = connection;
            _schema = schema;
            _serializer = serializer;
            _creation = creation;
        }



        public void Append<T>(Guid stream, T @event) where T : IEvent
        {
            var eventMapping = _schema.Events.EventMappingFor<T>();

            _connection.Execute(cmd => appendEvent(cmd, eventMapping, stream, @event));
        }

        public void AppendEvents(Guid stream, params IEvent[] events)
        {
            // TODO -- see if you can batch up the events into a single command
            // TODO -- TRANSACTIONAL INTEGRITY!
            events.Each(@event =>
            {
                var mapping = _schema.Events.EventMappingFor(@event.GetType());

                _connection.Execute(cmd => appendEvent(cmd, mapping, stream, @event));
            });
        }

        public Guid StartStream<T>(params IEvent[] events) where T : IAggregate
        {
            var stream = Guid.NewGuid();
            AppendEvents(stream, events);

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

        private void appendEvent(NpgsqlCommand conn, EventMapping eventMapping, Guid stream, IEvent @event)
        {
            if (@event.Id == Guid.Empty) @event.Id = Guid.NewGuid();
            throw new NotImplementedException("Need to redo a little bit here");
            /*
            conn.CallsSproc("mt_append_event")
                .With("stream", stream)
                .With("stream_type", eventMapping.Stream.StreamTypeName)
                .With("event_id", @event.Id)
                .With("event_type", eventMapping.EventTypeName)
                .With("body", _serializer.ToJson(@event), NpgsqlDbType.Jsonb)
                .ExecuteNonQuery();
                */
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

        //public IEventStoreAdmin Administration => this;

        //public ITransforms Transforms => this;

        public void LoadProjections(string directory)
        {
            _files.FindFiles(directory, FileSet.Deep("*.js")).Each(file =>
            {
                var body = _files.ReadStringFromFile(file);
                var name = Path.GetFileNameWithoutExtension(file);

                _connection.Execute(cmd =>
                {
                    cmd.CallsSproc("mt_load_projection_body")
                        .With("proj_name", name)
                        .With("body", body)
                        .ExecuteNonQuery();

                });
            });
        }

        public void LoadProjection(string file)
        {
            throw new NotImplementedException();
        }

        public void ClearAllProjections()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<ProjectionUsage> InitializeEventStoreInDatabase()
        {
            _connection.Execute(cmd =>
            {
                cmd.CallsSproc("mt_initialize_projections").ExecuteNonQuery();
            });

            return ProjectionUsages();
        }

        public IEnumerable<ProjectionUsage> ProjectionUsages()
        {
            var json = _connection.Execute(cmd => cmd.CallsSproc("mt_get_projection_usage").ExecuteScalar().As<string>());

            return _serializer.FromJson<ProjectionUsage[]>(json);
        }

        public void RebuildEventStoreSchema()
        {
            _creation.RunScript("mt_stream");
            _creation.RunScript("mt_initialize_projections");
            _creation.RunScript("mt_apply_transform");
            _creation.RunScript("mt_apply_aggregation");

            var js = SchemaBuilder.GetJavascript("mt_transforms");
            _connection.Execute(cmd =>
            {
                cmd.WithText("insert into mt_modules (name, definition) values (:name, :definition)")
                    .With("name", "mt_transforms")
                    .With("definition", js)
                    .ExecuteNonQuery();
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
                return cmd.CallsSproc("mt_apply_transform")
                    .With("stream_id", stream)
                    .With("event_id", @event.Id)
                    .With("projection", projectionName)
                    .With("event_type", eventType)
                    .With("event", eventJson, NpgsqlDbType.Json).ExecuteScalar();
            });

            return json.ToString();
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

        public TAggregate ApplySnapshot<TAggregate>(TAggregate aggregate, IEvent @event) where TAggregate : IAggregate
        {
            var aggregateId = aggregate.Id;
            var aggregateJson = _serializer.ToJson(aggregate);
            var projectionName = _schema.Events.StreamMappingFor<TAggregate>().StreamTypeName;

            var eventType = _schema.Events.EventMappingFor(@event.GetType()).EventTypeName;

            var json = _connection.Execute(cmd =>
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

        public void Dispose()
        {
            _connection.SafeDispose();
        }
    }
}