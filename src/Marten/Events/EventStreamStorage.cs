using System;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Events
{
    public class EventStreamStorage : IDocumentStorage, IdAssignment<EventStream>
    {
        private readonly EventGraph _graph;

        private FunctionName AppendEventFunction => new FunctionName(_graph.DatabaseSchemaName, "mt_append_event");

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

            var streamTypeName = stream.AggregateType == null ? null : _graph.AggregateAliasFor(stream.AggregateType);

            var eventTypes = stream.Events.Select(x => _graph.EventMappingFor(x.Data.GetType()).EventTypeName).ToArray();
            var bodies = stream.Events.Select(x => batch.Serializer.ToJson(x.Data)).ToArray();
            var ids = stream.Events.Select(x => x.Id).ToArray();

            batch.Sproc(AppendEventFunction)
                    .Param("stream", stream.Id)
                    .Param("stream_type", streamTypeName)
                    .Param("event_ids", ids)
                    .Param("event_types", eventTypes)
                    .JsonBodies("bodies", bodies);

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
            //EventStreams are not stored in entity map
        }

        public object Assign(EventStream document, out bool assigned)
        {
            assigned = false;
            return document.Id;
        }

        public void Assign(EventStream document, object id)
        {
            // nothing
        }
    }
}