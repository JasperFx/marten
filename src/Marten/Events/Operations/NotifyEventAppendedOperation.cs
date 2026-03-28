using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Daemon.HighWater;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

internal class NotifyEventAppendedOperation: IStorageOperation, NoDataReturnedCall
{
    private static readonly string Sql = $"select pg_notify('{PostgresqlListenWakeup.DefaultChannel}', '')";

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append(Sql);
    }

    public Type DocumentType => typeof(IEvent);

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
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
