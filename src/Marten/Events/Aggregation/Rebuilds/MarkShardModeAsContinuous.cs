using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Daemon;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Aggregation.Rebuilds;

internal class MarkShardModeAsContinuous : IStorageOperation
{
    private readonly ShardName _shardName;
    private readonly EventGraph _events;
    private readonly long _lastSequenceId;

    public MarkShardModeAsContinuous(ShardName shardName, EventGraph events, long lastSequenceId)
    {
        _shardName = shardName;
        _events = events;
        _lastSequenceId = lastSequenceId;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var parameters =
            builder.AppendWithParameters($"update {_events.ProgressionTable} set mode = '{ShardMode.continuous}', last_seq_id = ?, rebuild_threshold = 0 where name = ?");

        parameters[0].Value = _lastSequenceId;
        parameters[0].DbType = DbType.Int64;
        parameters[1].Value = _shardName.Identity;
        parameters[1].DbType = DbType.String;
    }

    public Type DocumentType => typeof(IEvent);
    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        throw new NotSupportedException();
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Other;
    }
}
