using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.Progress;

internal class DeleteProjectionProgress: IStorageOperation
{
    private readonly EventGraph _events;
    private readonly string _shardName;

    public DeleteProjectionProgress(EventGraph events, string shardName)
    {
        _events = events;
        _shardName = shardName;
    }

    public void ConfigureCommand(IPostgresqlCommandBuilder builder, IMartenSession session)
    {
        var parameters =
            builder.AppendWithParameters($"delete from {_events.ProgressionTable} where name = ?");

        parameters[0].Value = _shardName;
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

    public OperationRole Role()
    {
        return OperationRole.Events;
    }
}
