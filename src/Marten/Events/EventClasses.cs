using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using FubuCore;
using FubuCore.Util;
using Marten.Schema;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events
{
    public interface IEvent
    {
        Guid Id { get; set; }
    }

    public interface IAggregate
    {
        Guid Id { get; set; }
    }


    public interface IEvents
    {
        void Append<T>(Guid stream, T @event) where T : IEvent;

        void AppendEvents(Guid stream, params IEvent[] events);

        Guid StartStream<T>(params IEvent[] events) where T : IAggregate;

        T FetchSnapshot<T>(Guid streamId) where T : IAggregate;

        IEnumerable<IEvent> FetchStream<T>(Guid streamId) where T : IAggregate;

        void DeleteEvent<T>(Guid id);
        void DeleteEvent<T>(T @event) where T : IEvent;


        void ReplaceEvent<T>(T @event);
    }

    public class Events : IEvents
    {
        private readonly ICommandRunner _runner;
        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;

        public Events(ICommandRunner runner, IDocumentSchema schema, ISerializer serializer)
        {
            _runner = runner;
            _schema = schema;
            _serializer = serializer;
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
            cmd.AddParameter("body", _serializer.ToJson(@event)).NpgsqlDbType = NpgsqlDbType.Json;

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
    }

    public enum ProjectionTiming
    {
        inline,
        live,
        async
    }


    public class EventGraph
    {
        private readonly Cache<string, EventMapping> _byEventName = new Cache<string, EventMapping>();
        private readonly Cache<Type, EventMapping> _events = new Cache<Type, EventMapping>();

        private readonly Cache<Type, StreamMapping> _streams =
            new Cache<Type, StreamMapping>(type => new StreamMapping(type));

        public EventGraph()
        {
            _events.OnMissing = eventType =>
            {
                var stream = _streams.FirstOrDefault(x => x.HasEventType(eventType));

                return stream?.EventMappingFor(eventType);
            };

            _byEventName.OnMissing = name => { return AllEvents().FirstOrDefault(x => x.EventTypeName == name); };
        }

        public StreamMapping StreamMappingFor(Type aggregateType)
        {
            return _streams[aggregateType];
        }

        public StreamMapping StreamMappingFor<T>() where T : IAggregate
        {
            return StreamMappingFor(typeof (T));
        }

        public EventMapping EventMappingFor(Type eventType)
        {
            return _events[eventType];
        }

        public EventMapping EventMappingFor<T>() where T : IEvent
        {
            return EventMappingFor(typeof (T));
        }

        public IEnumerable<EventMapping> AllEvents()
        {
            return _streams.SelectMany(x => x.AllEvents());
        }

        public EventMapping EventMappingFor(string eventType)
        {
            return _byEventName[eventType];
        }
    }


    public class StreamMapping : DocumentMapping
    {
        private readonly Cache<Type, EventMapping> _events = new Cache<Type, EventMapping>();

        public StreamMapping(Type aggregateType) : base(aggregateType)
        {
            if (!aggregateType.CanBeCastTo<IAggregate>())
                throw new ArgumentOutOfRangeException(nameof(aggregateType),
                    $"Only types implementing {typeof (IAggregate)} can be accepted");


            _events.OnMissing = type => { return new EventMapping(this, type); };

            StreamTypeName = aggregateType.Name.SplitPascalCase().ToLower().Replace(" ", "_");
        }

        public string StreamTypeName { get; set; }

        public EventMapping AddEvent(Type eventType)
        {
            return _events[eventType];
        }

        public EventMapping EventMappingFor(Type eventType)
        {
            return _events[eventType];
        }


        public bool HasEventType(Type eventType)
        {
            return _events.Has(eventType);
        }

        public IEnumerable<EventMapping> AllEvents()
        {
            return _events;
        }
    }

    public class EventMapping : DocumentMapping
    {
        public EventMapping(StreamMapping parent, Type eventType) : base(eventType)
        {
            if (!eventType.CanBeCastTo<IEvent>())
                throw new ArgumentOutOfRangeException(nameof(eventType),
                    $"Only types implementing {typeof (IEvent)} can be accepted");

            Stream = parent;

            EventTypeName = eventType.Name.SplitPascalCase().ToLower().Replace(" ", "_");
        }

        public string EventTypeName { get; set; }

        public StreamMapping Stream { get; }
    }
}