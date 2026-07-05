using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Postgresql;

using Marten.Services;

namespace Marten.Events.Protected;

internal class OverwriteEventOperation : IStorageOperation, NoDataReturnedCall
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
        // Bind both data and headers as direct UTF-8 bytes via WriteToParameter so we
        // skip the intermediate string allocations Serializer.ToJson would produce.
        if (_graph.MetadataConfig.HeadersEnabled)
        {
            var parameters = builder.AppendWithParameters($"update {_graph.DatabaseSchemaName}.mt_events set data = ?, headers = ? where seq_id = ?");
            session.Serializer.WriteToParameter(parameters[0], _e.Data);
            session.Serializer.WriteToParameter(parameters[1], _e.Headers);
            parameters[2].NpgsqlDbType = NpgsqlDbType.Bigint;
            parameters[2].Value = _e.Sequence;
        }
        else
        {
            var parameters = builder.AppendWithParameters($"update {_graph.DatabaseSchemaName}.mt_events set data = ? where seq_id = ?");
            session.Serializer.WriteToParameter(parameters[0], _e.Data);
            parameters[1].NpgsqlDbType = NpgsqlDbType.Bigint;
            parameters[1].Value = _e.Sequence;
        }
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }
}
