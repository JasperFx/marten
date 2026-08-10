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
    // #5210 — this operation is a NoDataReturnedCall, so OperationPage.ApplyCallbacksAsync
    // never advances the batched reader past it. The SQL therefore MUST NOT produce a
    // result set. The original `select pg_notify(...)` form returned a one-row result set
    // (a single `void` column), which left the reader one result set behind and made any
    // data-returning operation later in the same batch (an inline projection upsert, a
    // revisioned document update, ...) read the pg_notify row instead of its own RETURNING
    // row. The DO/PERFORM form fires the identical NOTIFY without returning anything.
    internal static readonly string Sql =
        $"DO $$ BEGIN PERFORM pg_notify('{PostgresqlListenWakeup.DefaultChannel}', ''); END $$";

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append(Sql);
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
