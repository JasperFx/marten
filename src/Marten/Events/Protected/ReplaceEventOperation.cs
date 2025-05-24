using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Protected;

internal class ReplaceEventOperation<T> : IStorageOperation where T : class
{
    private readonly EventGraph _graph;
    private readonly T _eventBody;
    private readonly long _sequence;
    private readonly string _eventTypeName;
    private readonly string _dotNetType;

    public ReplaceEventOperation(EventGraph graph, T eventBody, long sequence)
    {
        _graph = graph;
        _eventBody = eventBody;
        _sequence = sequence;
        var mapping = graph.EventMappingFor<T>();
        _eventTypeName = mapping.EventTypeName;
        _dotNetType = mapping.DotNetTypeName;

        Id = CombGuidIdGeneration.NewGuid();
    }

    public Guid Id { get; }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append($"update {_graph.DatabaseSchemaName}.mt_events set data = ");
        builder.AppendParameter(session.Serializer.ToJson(_eventBody), NpgsqlDbType.Jsonb);
        builder.Append(", timestamp = now() at time zone 'utc', type = ");
        builder.AppendParameter(_eventTypeName, NpgsqlDbType.Varchar);
        builder.Append(", mt_dotnet_type = ");
        builder.AppendParameter(_dotNetType, NpgsqlDbType.Varchar);
        builder.Append(", id = ");
        builder.AppendParameter(Id);

        if (_graph.MetadataConfig.HeadersEnabled)
        {
            builder.Append(", headers = '{}'");
        }

        if (_graph.MetadataConfig.CausationIdEnabled)
        {
            builder.Append(", causation_id = NULL");
        }

        if (_graph.MetadataConfig.CorrelationIdEnabled)
        {
            builder.Append(", correlation_id = NULL");
        }

        builder.Append(" where seq_id = ");
        builder.AppendParameter(_sequence);
    }

    public Type DocumentType => typeof(IEvent);
    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // Nothing
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}
