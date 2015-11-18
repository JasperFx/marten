using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using FubuCore;
using Marten.Schema;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events
{
    public class EventStore : IEventStore, IEventStoreAdmin
    {
        private readonly ICommandRunner _runner;
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;
        private readonly IDocumentSchemaCreation _creation;

        public EventStore(ICommandRunner runner, IDocumentSchema schema, ISerializer serializer, IDocumentSchemaCreation creation)
        {
            _runner = runner;
            _schema = schema;
            _serializer = serializer;
            _creation = creation;
        }

        public void Append<T>(Guid stream, T @event) where T : IEvent
        {
            var eventMapping = _schema.Events.EventMappingFor<T>();

            _runner.Execute(conn => { appendEvent(conn, eventMapping, stream, @event); });
        }

        public void AppendEvents(Guid stream, params IEvent[] events)
        {
            _runner.Execute(conn =>
            {
                // TODO -- this workflow is getting common. Maybe pull this into CommandRunner
                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        events.Each(@event =>
                        {
                            var mapping = _schema.Events.EventMappingFor(@event.GetType());

                            appendEvent(conn, mapping, stream, @event);
                        });

                        tx.Commit();
                    }
                    catch (Exception)
                    {
                        tx.Rollback();
                        throw;
                    }
                }
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
            return _runner.Execute(conn =>
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = "select type, data from mt_events where stream_id = :id order by version";
                cmd.AddParameter("id", streamId);

                using (var reader = cmd.ExecuteReader())
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

        private void appendEvent(NpgsqlConnection conn, EventMapping eventMapping, Guid stream, IEvent @event)
        {
            if (@event.Id == Guid.Empty) @event.Id = Guid.NewGuid();

            var cmd = conn.CreateCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "mt_append_event";
            cmd.AddParameter("stream", stream);
            cmd.AddParameter("stream_type", eventMapping.Stream.StreamTypeName);
            cmd.AddParameter("event_id", @event.Id);
            cmd.AddParameter("event_type", eventMapping.EventTypeName);
            cmd.AddParameter("body", _serializer.ToJson(@event)).NpgsqlDbType = NpgsqlDbType.Jsonb;

            cmd.ExecuteNonQuery();
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

        public IEventStoreAdmin Administration => this;

        public void LoadProjections(string directory)
        {
            var files = new FileSystem();

            files.FindFiles(directory, FileSet.Deep("*.js")).Each(file =>
            {
                var body = files.ReadStringFromFile(file);
                var name = Path.GetFileNameWithoutExtension(file);

                _runner.Execute(conn =>
                {
                    conn.CreateSprocCommand("mt_load_projection_body")
                        .WithParameter("proj_name", name)
                        .WithParameter("body", body)
                        .ExecuteNonQuery();

                });
            });
        }

        public void ClearAllProjections()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<ProjectionUsage> ProjectionUsages()
        {
            throw new NotImplementedException();
        }

        public void RebuildEventStoreSchema()
        {
            _creation.RunScript("mt_stream");

            var js = SchemaBuilder.GetJavascript("mt_transforms");
            _runner.Execute(conn =>
            {
                conn.CreateCommand("insert into mt_modules (name, definition) values (:name, :definition)")
                    .WithParameter(":name", "mt_transforms")
                    .WithParameter("definition", js)
                    .ExecuteNonQuery();
            });
        }
    }
}