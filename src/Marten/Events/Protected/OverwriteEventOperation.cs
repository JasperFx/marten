using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Operations;
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
        builder.Append($"update {_graph.DatabaseSchemaName}.mt_events set ");
        StoredEventPayload.AppendAssignments(builder, session, _graph.EventMappingFor(_e.EventType), _e.Data);

        if (_graph.MetadataConfig.HeadersEnabled)
        {
            // Headers as direct UTF-8 bytes, skipping the string Serializer.ToJson would allocate
            var headers = builder.AppendWithParameters(", headers = ?");
            session.Serializer.WriteToParameter(headers[0], _e.Headers);
        }

        builder.Append(" where seq_id = ");
        builder.AppendParameter(_e.Sequence, NpgsqlDbType.Bigint);

        // #5234: seq_id alone is not unique across tenants under UseTenantPartitionedEvents
        builder.AppendConjoinedTenantFilter(session);
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
