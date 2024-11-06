using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.Operations;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core.Operations;
using Weasel.Postgresql;

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

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        if (_graph.MetadataConfig.HeadersEnabled)
        {
            var parameters = builder.AppendWithParameters($"update {_graph.DatabaseSchemaName}.mt_events set data = ?, headers = ? where seq_id = ?");
            builder.SetParameterAsJson(parameters[0], session.Serializer.ToJson(_e.Data));
            builder.SetParameterAsJson(parameters[1], session.Serializer.ToJson(_e.Headers));
            parameters[2].Value = _e.Sequence;
            parameters[2].DbType = DbType.Int64;
        }
        else
        {
            var parameters = builder.AppendWithParameters($"update {_graph.DatabaseSchemaName}.mt_events set data = ? where seq_id = ?");
            builder.SetParameterAsJson(parameters[0], session.Serializer.ToJson(_e.Data));
            parameters[1].Value = _e.Sequence;
            parameters[1].DbType = DbType.Int64;
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
