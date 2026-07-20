using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Internals;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.Progress;

internal class UpdateProjectionProgress: IStorageOperation, AssertsOnCallback, NoDataReturnedCall
{
    private readonly EventGraph _events;

    public UpdateProjectionProgress(EventGraph events, EventRange range)
    {
        Range = range;
        _events = events;
    }

    public EventRange Range { get; }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        // #4596 Session 3: per-tenant progression keying flows naturally
        // through ShardName.Identity — per-tenant shards (Phase 2) get a
        // distinct Identity per (projection, shardKey, tenant) so the same
        // WHERE name = ? optimistic update naturally scopes to one tenant.
        // #4981: advance last_updated on every progress write so it reflects the last time this
        // shard actually moved. Previously only last_seq_id was set, so daemon shard rows froze
        // last_updated at insert time and it looked exactly like a stalled projection. Matches
        // HighWaterDetector / mt_mark_event_progression, which already use transaction_timestamp().
        var parameters =
            builder.AppendWithParameters(
                $"update {_events.ProgressionTable} set last_seq_id = ?, last_updated = transaction_timestamp() where name = ? and last_seq_id = ?");

        parameters[0].Value = Range.SequenceCeiling;
        parameters[0].NpgsqlDbType = NpgsqlDbType.Bigint;
        parameters[1].Value = Range.ShardName.Identity;
        parameters[1].NpgsqlDbType = NpgsqlDbType.Varchar;
        parameters[2].Value = Range.SequenceFloor;
        parameters[2].NpgsqlDbType = NpgsqlDbType.Bigint;
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (reader.RecordsAffected ==
            0) // There's some weird quirks of the combined statements where this could be erroneously 2
        {
            throw new ProgressionProgressOutOfOrderException(Range.ShardName);
        }

        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }
}
