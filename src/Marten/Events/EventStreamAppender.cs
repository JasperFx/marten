using System;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Services;

namespace Marten.Events
{
    public class EventStreamAppender : IDocumentUpsert
    {
        private readonly EventGraph _graph;

        public EventStreamAppender(EventGraph graph)
        {
            _graph = graph;

            if (_graph.JavascriptProjectionsEnabled)
            {
                throw new NotSupportedException("Marten does not yet support Javascript projections");
            }

            AppendEventFunction = new FunctionName(_graph.DatabaseSchemaName, "mt_append_event");
        }

        public FunctionName AppendEventFunction { get; }

        public void RegisterUpdate(UpdateBatch batch, object entity)
        {
            var stream = entity.As<EventStream>();

            var streamTypeName = stream.AggregateType == null ? null : _graph.AggregateAliasFor(stream.AggregateType);

            var eventTypes = stream.Events.Select(x => _graph.EventMappingFor(x.Data.GetType()).EventTypeName).ToArray();
            var bodies = stream.Events.Select(x => batch.Serializer.ToJson(x.Data)).ToArray();
            var ids = stream.Events.Select(x => x.Id).ToArray();

            batch.Sproc(AppendEventFunction, new EventStreamVersioningCallback(stream))
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
    }
}