using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.Progress;

internal class InsertProjectionProgress: IStorageOperation, AssertsOnCallback, NoDataReturnedCall
{
    private readonly EventGraph _events;
    private readonly EventRange _progress;

    public InsertProjectionProgress(EventGraph events, EventRange progress)
    {
        _events = events;
        _progress = progress;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        // #4596 Session 3: per-tenant progression keying flows naturally
        // through ShardName.Identity. When the per-tenant daemon (Phase 2)
        // creates shards with tenant ids populated, ShardName.Compose emits
        // identities of the form `{Name}:{ShardKey}:{tenantId}` so the
        // `name` column distinguishes per-tenant rows without any tenant_id
        // column on this table. No code change needed here — Identity does
        // the right thing for both today's tenant-less and tomorrow's
        // tenant-bearing shards.
        //
        // marten#6127: a shard that failed before its first commit has a row at 0 that only carries the failure
        // (MartenDatabase.WriteExtendedProgressionAsync). It has no progress in it, so the first commit deletes it
        // and inserts as usual. A row past 0 is left alone, and the insert fails on it exactly as it always has
        // when a second daemon committed first. Both statements run in the batch's transaction.
        var deletion = builder.AppendWithParameters(
            $"delete from {_events.ProgressionTable} where name = ? and last_seq_id = 0");
        deletion[0].Value = _progress.ShardName.Identity;
        deletion[0].NpgsqlDbType = NpgsqlDbType.Varchar;

        builder.StartNewCommand();

        var parameters =
            builder.AppendWithParameters($"insert into {_events.ProgressionTable} (name, last_seq_id) values (?, ?)");

        parameters[0].Value = _progress.ShardName.Identity;
        parameters[0].NpgsqlDbType = NpgsqlDbType.Varchar;
        parameters[1].Value = _progress.SequenceCeiling;
        parameters[1].NpgsqlDbType = NpgsqlDbType.Bigint;
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
