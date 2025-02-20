using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Aggregation.Rebuilds;

internal class BackfillStreamTypeOperation: IStorageOperation
{
    private readonly ILogger _logger;
    private readonly long _floor;
    private readonly long _ceiling;
    private readonly string _streamType;
    private readonly string[] _eventTypeNames;
    private readonly string _schemaName;
    private readonly TenancyStyle _tenancy;

    public BackfillStreamTypeOperation(ILogger logger, long floor, long ceiling, EventGraph events, IAggregateProjection projection)
    {
        _tenancy = events.TenancyStyle;
        _schemaName = events.DatabaseSchemaName;
        _logger = logger;
        _floor = floor;
        _ceiling = ceiling;
        _streamType = events.AggregateAliasFor(projection.AggregateType);
        _eventTypeNames = projection.AllEventTypes.Select(x => events.EventMappingFor((Type)x).EventTypeName).ToArray();
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append($"update {_schemaName}.mt_streams s set type = ");
        builder.AppendParameter(_streamType);
        builder.Append($" from {_schemaName}.mt_events e where s.id = e.stream_id");

        if (_tenancy == TenancyStyle.Conjoined)
        {
            builder.Append($" and s.tenant_id = e.tenant_id");
        }

        builder.Append($" and s.type is NULL and e.type = ANY(");
        builder.AppendParameter(_eventTypeNames, NpgsqlDbType.Array | NpgsqlDbType.Text);
        builder.Append(") and s.is_archived = FALSE and e.seq_id > ");
        builder.AppendParameter(_floor);
        builder.Append(" and e.seq_id <= ");
        builder.AppendParameter(_ceiling);
    }

    public Type DocumentType => typeof(IEvent);
    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // Nothing
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        // TODO -- log something here
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Other;
}
