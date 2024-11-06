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

internal class MarkShardModeAsRebuilding : IStorageOperation
{
    private readonly ShardName _shardName;
    private readonly EventGraph _events;
    private readonly long _rebuildThreshold;

    public MarkShardModeAsRebuilding(ShardName shardName, EventGraph events, long rebuildThreshold)
    {
        _shardName = shardName;
        _events = events;
        _rebuildThreshold = rebuildThreshold;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var parameters =
            builder.AppendWithParameters($"insert into {_events.ProgressionTable} (name, last_seq_id, mode, rebuild_threshold) values (?, 0, '{ShardMode.rebuilding}', ?) on conflict (name) do update set mode = '{ShardMode.rebuilding}', last_seq_id = 0, rebuild_threshold = ?");

        parameters[0].Value = _shardName.Identity;
        parameters[1].Value = _rebuildThreshold;
        parameters[1].DbType = DbType.Int64;
        parameters[2].Value = _rebuildThreshold;
        parameters[2].DbType = DbType.Int64;
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
