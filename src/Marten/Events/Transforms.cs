using System;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Events
{
    public class Transforms : ITransforms
    {
        private FunctionName ApplyTransformFunction
            => new FunctionName(_schema.Events.DatabaseSchemaName, "mt_apply_transform");

        private FunctionName ApplyAggregationFunction
            => new FunctionName(_schema.Events.DatabaseSchemaName, "mt_apply_aggregation");

        private FunctionName StartAggregationFunction
            => new FunctionName(_schema.Events.DatabaseSchemaName, "mt_start_aggregation");


        private readonly IDocumentSchema _schema;
        private readonly ISerializer _serializer;
        private readonly IManagedConnection _connection;

        public Transforms(IDocumentSchema schema, ISerializer serializer, IManagedConnection connection)
        {
            _schema = schema;
            _serializer = serializer;
            _connection = connection;
        }

        public string Transform(string projectionName, Guid streamId, IEvent @event)
        {
            var mapping = _schema.Events.EventMappingFor(@event.Data.GetType());
            var eventType = mapping.EventTypeName;

            var eventJson = _serializer.ToJson(@event.Data);

            var json = _connection.Execute(cmd =>
            {
                return cmd.CallsSproc(ApplyTransformFunction)
                    .With("stream_id", streamId)
                    .With("event_id", @event.Id)
                    .With("projection", projectionName)
                    .With("event_type", eventType)
                    .With("event", eventJson, NpgsqlDbType.Json).ExecuteScalar();
            });

            return json.ToString();
        }

        public TAggregate ApplySnapshot<TAggregate>(Guid streamId, TAggregate aggregate, IEvent @event)
            where TAggregate : class, new()
        {
            var aggregateJson = _serializer.ToJson(aggregate);
            var projectionName = _schema.Events.AggregateFor<TAggregate>().Alias;

            var eventType = _schema.Events.EventMappingFor(@event.Data.GetType()).EventTypeName;

            string json = _connection.Execute(cmd =>
            {
                return cmd.CallsSproc(ApplyAggregationFunction)
                    .With("stream_id", streamId)
                    .With("event_id", @event.Id)
                    .With("projection", projectionName)
                    .With("event_type", eventType)
                    .With("event", _serializer.ToJson(@event.Data), NpgsqlDbType.Json)
                    .With("aggregate", aggregateJson, NpgsqlDbType.Json).ExecuteScalar().As<string>();
            });

            return _serializer.FromJson<TAggregate>(json);
        }

        public TAggregate StartSnapshot<TAggregate>(Guid streamId, IEvent @event) where TAggregate : class, new()
        {
            var projectionName = _schema.Events.AggregateFor<TAggregate>().Alias;

            var eventType = _schema.Events.EventMappingFor(@event.Data.GetType()).EventTypeName;

            var json = _connection.Execute(cmd =>
            {
                return cmd.CallsSproc(StartAggregationFunction)
                    .With("stream_id", streamId)
                    .With("event_id", @event.Id)
                    .With("projection", projectionName)
                    .With("event_type", eventType)
                    .With("event", _serializer.ToJson(@event.Data), NpgsqlDbType.Json)
                    .ExecuteScalar().As<string>();
            });

            return _serializer.FromJson<TAggregate>(json);
        }
    }
}