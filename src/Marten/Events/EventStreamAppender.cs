using System;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Services;

namespace Marten.Events
{
    public class EventStreamAppender
    {
        private readonly EventGraph _graph;

        public EventStreamAppender(EventGraph graph)
        {
            _graph = graph;

            AppendEventFunction = new DbObjectName(_graph.DatabaseSchemaName, "mt_append_event");
        }

        public DbObjectName AppendEventFunction { get; }

        public void RegisterUpdate(UpdateBatch batch, object entity)
        {
            var stream = entity.As<EventStream>();

            var streamTypeName = stream.AggregateType == null ? null : _graph.AggregateAliasFor(stream.AggregateType);

            var events = stream.Events.ToArray();
            var eventTypes = events.Select(x => _graph.EventMappingFor(x.Data.GetType()).EventTypeName).ToArray();
            var dotnetTypes = events.Select(x => _graph.DotnetTypeNameFor(x.Data.GetType())).ToArray();

            var ids = events.Select(x => x.Id).ToArray();

            var sprocCall = toAppendSprocCall(batch, stream, streamTypeName, ids, eventTypes, dotnetTypes);

            AddJsonBodies(batch, sprocCall, events);
        }

        private SprocCall toAppendSprocCall(UpdateBatch batch, EventStream stream, string streamTypeName, Guid[] ids, string[] eventTypes, string[] dotnetTypes)
        {
            if (_graph.StreamIdentity == StreamIdentity.AsGuid)
            {
                return batch.Sproc(AppendEventFunction, new EventStreamVersioningCallback(stream))
                                     .Param("stream", stream.Id)
                                     .Param("stream_type", streamTypeName)
                                     .Param(TenantIdArgument.ArgName, batch.TenantId)
                                     .Param("event_ids", ids)
                                     .Param("event_types", eventTypes)
                                     .Param("dotnet_types", dotnetTypes);
            }


            return batch.Sproc(AppendEventFunction, new EventStreamVersioningCallback(stream))
                        .Param("stream", stream.Key)
                        .Param("stream_type", streamTypeName)
                        .Param(TenantIdArgument.ArgName, batch.TenantId)
                        .Param("event_ids", ids)
                        .Param("event_types", eventTypes)
                        .Param("dotnet_types", dotnetTypes);
        }

        static void AddJsonBodies(UpdateBatch batch, SprocCall sprocCall, IEvent[] events)
        {
            var serializer = batch.Serializer;

            if (batch.UseCharBufferPooling)
            {
                var segments = new ArraySegment<char>[events.Length];
                for (var i = 0; i < events.Length; i++)
                {
                    segments[i] = SerializeToCharArraySegment(batch, serializer, events[i].Data);
                }
                sprocCall.JsonBodies("bodies", segments);
            }
            else
            {
                sprocCall.JsonBodies("bodies", events.Select(x => serializer.ToJson(x.Data)).ToArray());
            }
        }

        static ArraySegment<char> SerializeToCharArraySegment(UpdateBatch batch, ISerializer serializer, object data)
        {
            var writer = batch.GetWriter();
            serializer.ToJson(data, writer);
            return writer.ToCharSegment();
        }
    }
}