using System;
using System.Collections.Generic;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events
{
    public interface IAggregateStorage : IDocumentStorage
    {
        Type AggregateType { get; }
        string StreamTypeName { get; set; }
        EventMapping AddEvent(Type eventType);
        EventMapping EventMappingFor(Type eventType);
        bool HasEventType(Type eventType);
        IEnumerable<EventMapping> AllEvents();
    }

    public class AggregateStorage<T> : IAggregateStorage where T : IAggregate 
    {
        private readonly Cache<Type, EventMapping> _events = new Cache<Type, EventMapping>();

        public AggregateStorage()
        {
            AggregateType = typeof(T);

            _events.OnMissing = type => new EventMapping(type);

            StreamTypeName = AggregateType.Name.SplitPascalCase().ToLower().Replace(" ", "_");
        }

        public Type AggregateType { get;}

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

        public Type DocumentType { get; } = typeof (Stream<T>);
        public NpgsqlDbType IdType { get; } = NpgsqlDbType.Uuid;

        public NpgsqlCommand LoaderCommand(object id)
        {
            throw new NotImplementedException();
        }

        public NpgsqlCommand DeleteCommandForId(object id)
        {
            throw new NotImplementedException();
        }

        public NpgsqlCommand DeleteCommandForEntity(object entity)
        {
            throw new NotImplementedException();
        }

        public NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids)
        {
            throw new NotImplementedException();
        }

        public object Identity(object document)
        {
            return document.As<Stream<T>>().Id;
            
        }

        public void RegisterUpdate(UpdateBatch batch, object entity)
        {
            var stream = entity.As<Stream<T>>();
            stream.Events.Each(@event =>
            {
                // TODO -- what if this doesn't exist? Get it lazily?
                var mapping = EventMappingFor(@event.GetType());

                batch.Sproc("mt_append_event")
                .Param("stream", stream.Id)
                .Param("stream_type", StreamTypeName)
                .Param("event_id", @event.Id)
                .Param("event_type", mapping.EventTypeName)
                .JsonEntity("body", @event);

            });
        }

        public void RegisterUpdate(UpdateBatch batch, object entity, string json)
        {
            throw new NotSupportedException();
        }

        public void Remove(IIdentityMap map, object entity)
        {
            throw new NotImplementedException();
        }

        public void Delete(IIdentityMap map, object id)
        {
            throw new NotImplementedException();
        }

        public void Store(IIdentityMap map, object id, object entity)
        {
            throw new NotImplementedException();
        }
    }
}