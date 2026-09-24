#nullable enable
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

internal class UpdateProjectionProgress: IStorageOperation, AssertsOnCallback, NoDataReturnedCall,
    IAssertsRecordsAffected
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
        // #5502: reader.RecordsAffected is CUMULATIVE across the batch, so this only answers the
        // question when nothing before this operation affected a row -- which in practice means it
        // only answers it for the first operation on the page. The honest per-statement check is
        // DetectRecordsAffectedProblem below, which OperationPage runs for every operation once the
        // reader has been drained. This one stays as it is so that an out-of-order progression on
        // the first operation keeps throwing exactly as it always has.
        if (reader.RecordsAffected == 0)
        {
            throw new ProgressionProgressOutOfOrderException(Range.ShardName);
        }

        return Task.CompletedTask;
    }

    public Exception? DetectRecordsAffectedProblem(int recordsAffected)
    {
        // -1 is "the database did not say", not "nothing matched".
        if (recordsAffected != 0)
        {
            return null;
        }

        // The optimistic update -- "set last_seq_id = ceiling where name = ? and last_seq_id = ?" --
        // matched no row, so the stored progression is not the floor this range was built from and
        // this shard's progress did not move.
        //
        // The three-argument overload rather than the ShardName one: it keeps the floor and ceiling
        // on the exception, and "expected floor 5 but it had already moved, attempted ceiling 50" is
        // what actually tells an operator whether they are looking at a second daemon or a loader
        // handing out the wrong floor. PostprocessAsync above deliberately keeps the older message,
        // because that one is thrown and its wording is long-standing behaviour.
        return new ProgressionProgressOutOfOrderException(Range.ShardName.Identity, Range.SequenceFloor,
            Range.SequenceCeiling);
    }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }
}
