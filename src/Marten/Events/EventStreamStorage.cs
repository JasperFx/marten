using System;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events
{
    public class EventStreamStorage : IDocumentStorage
    {
        private readonly EventGraph _graph;

        public EventStreamStorage(EventGraph graph)
        {
            _graph = graph;
        }

        public Type DocumentType { get; } = typeof (EventStream);
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
            return document.As<EventStream>().Id;
        }

        public void RegisterUpdate(UpdateBatch batch, object entity)
        {
            var stream = entity.As<EventStream>();
            stream.Events.Each(@event =>
            {
                throw new NotImplementedException();

                /*
                // TODO -- what if this doesn't exist? Get it lazily?
                var mapping = EventMappingFor(@event.GetType());

                batch.Sproc("mt_append_event")
                .Param("stream", stream.Id)
                .Param("stream_type", StreamTypeName)
                .Param("event_id", @event.Id)
                .Param("event_type", mapping.EventTypeName)
                .JsonEntity("body", @event);
                */
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