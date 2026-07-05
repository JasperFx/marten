using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Postgresql;

using Marten.Services;

namespace Marten.Events.Daemon.Progress;

internal class DeleteProjectionProgress: IStorageOperation, NoDataReturnedCall
{
    private readonly EventGraph _events;
    private readonly string _shardName;

    public DeleteProjectionProgress(EventGraph events, string shardName)
    {
        _events = events;
        _shardName = shardName;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        // #4596 Session 3: per-tenant scoping flows through the shard name
        // itself. Callers wanting to delete one tenant's progression pass the
        // tenant-bearing ShardName.Identity (e.g. "OrdersProjection:All:alpha").
        // No tenant_id column on mt_event_progression — see EventProgressionTable.
        var parameters =
            builder.AppendWithParameters($"delete from {_events.ProgressionTable} where name = ?");

        parameters[0].Value = _shardName;
        parameters[0].NpgsqlDbType = NpgsqlDbType.Varchar;
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
