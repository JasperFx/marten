using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Core.Operations;
using Weasel.Postgresql;
using ICommandBuilder = Weasel.Postgresql.ICommandBuilder;

namespace Marten.Events.Protected;

internal class OverwriteEventOperation : IStorageOperation
{
    private readonly EventGraph _graph;
    private readonly IEvent _e;

    public OverwriteEventOperation(EventGraph graph, IEvent e)
    {
        _graph = graph;
        _e = e;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        if (_graph.MetadataConfig.HeadersEnabled)
        {
            var parameters = builder.AppendWithParameters($"update {_graph.DatabaseSchemaName}.mt_events set data = ?, headers = ? where seq_id = ?");
            parameters[0].NpgsqlDbType = NpgsqlDbType.Jsonb;
            parameters[0].Value = session.Serializer.ToJson(_e.Data);
            parameters[1].NpgsqlDbType = NpgsqlDbType.Jsonb;
            parameters[1].Value = session.Serializer.ToJson(_e.Headers);
            parameters[2].NpgsqlDbType = NpgsqlDbType.Bigint;
            parameters[2].Value = _e.Sequence;
        }
        else
        {
            var parameters = builder.AppendWithParameters($"update {_graph.DatabaseSchemaName}.mt_events set data = ? where seq_id = ?");
            parameters[0].NpgsqlDbType = NpgsqlDbType.Jsonb;
            parameters[0].Value = session.Serializer.ToJson(_e.Data);
            parameters[1].NpgsqlDbType = NpgsqlDbType.Bigint;
            parameters[1].Value = _e.Sequence;
        }


    }

    public Type DocumentType => typeof(IEvent);
    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // nothing
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }
}
