#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.Parking;

/// <summary>
/// marten#6003: write (insert or replace) a parked identity's row inside the projection batch that moves the shard's
/// progression past the failing event.
/// </summary>
internal class ParkIdentityOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly EventGraph _events;
    private readonly ParkedIdentity _parked;

    public ParkIdentityOperation(EventGraph events, ParkedIdentity parked)
    {
        _events = events;
        _parked = parked;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var row = _parked;
        builder.Append($"insert into {_events.DatabaseSchemaName}.{ParkedIdentitiesTable.Name} ");
        builder.Append(
            "(projection_name, projection_version, shard_key, tenant_id, identity, identity_type, first_failing_seq, resume_after, failure_category, exception_type, exception_message, event_type, event_id, failing_stream, parked_at, replay_attempts, last_attempt_at, last_attempt_error, requested_action, requested_at) values (");

        builder.AppendParameter(row.ProjectionName, NpgsqlDbType.Varchar);
        builder.Append(", ");
        builder.AppendParameter((int)row.ProjectionVersion, NpgsqlDbType.Integer);
        builder.Append(", ");
        builder.AppendParameter(row.ShardKey, NpgsqlDbType.Varchar);
        builder.Append(", ");
        builder.AppendParameter(row.TenantId, NpgsqlDbType.Varchar);
        builder.Append(", ");
        builder.AppendParameter(row.IdentityText, NpgsqlDbType.Varchar);
        builder.Append(", ");
        builder.AppendParameter(row.IdentityType, NpgsqlDbType.Varchar);
        builder.Append(", ");
        builder.AppendParameter(row.FirstFailingSequence, NpgsqlDbType.Bigint);
        builder.Append(", ");
        builder.AppendParameter(row.ResumeAfter, NpgsqlDbType.Bigint);
        builder.Append(", ");
        builder.AppendParameter(row.Category.ToString(), NpgsqlDbType.Varchar);
        builder.Append(", ");
        builder.AppendParameter((object?)row.ExceptionType ?? DBNull.Value, NpgsqlDbType.Varchar);
        builder.Append(", ");
        builder.AppendParameter((object?)row.ExceptionMessage ?? DBNull.Value, NpgsqlDbType.Text);
        builder.Append(", ");
        builder.AppendParameter((object?)row.EventTypeName ?? DBNull.Value, NpgsqlDbType.Varchar);
        builder.Append(", ");
        builder.AppendParameter(row.EventId.HasValue ? row.EventId.Value : DBNull.Value, NpgsqlDbType.Uuid);
        builder.Append(", ");
        builder.AppendParameter((object?)row.FailingStream ?? DBNull.Value, NpgsqlDbType.Varchar);
        builder.Append(", ");

        if (row.ParkedAt == default)
        {
            builder.Append("transaction_timestamp()");
        }
        else
        {
            builder.AppendParameter(row.ParkedAt, NpgsqlDbType.TimestampTz);
        }

        builder.Append(", ");
        builder.AppendParameter(row.ReplayAttempts, NpgsqlDbType.Integer);
        builder.Append(", ");
        builder.AppendParameter(row.LastAttemptAt.HasValue ? row.LastAttemptAt.Value : DBNull.Value,
            NpgsqlDbType.TimestampTz);
        builder.Append(", ");
        builder.AppendParameter((object?)row.LastAttemptError ?? DBNull.Value, NpgsqlDbType.Text);
        builder.Append(", ");
        builder.AppendParameter(row.RequestedAction.HasValue ? row.RequestedAction.Value.ToString() : DBNull.Value,
            NpgsqlDbType.Varchar);
        builder.Append(", ");
        builder.AppendParameter(row.RequestedAt.HasValue ? row.RequestedAt.Value : DBNull.Value,
            NpgsqlDbType.TimestampTz);

        builder.Append(
            ") on conflict (projection_name, projection_version, shard_key, tenant_id, identity) do update set " +
            "identity_type = excluded.identity_type, first_failing_seq = excluded.first_failing_seq, " +
            "resume_after = excluded.resume_after, failure_category = excluded.failure_category, " +
            "exception_type = excluded.exception_type, exception_message = excluded.exception_message, " +
            "event_type = excluded.event_type, event_id = excluded.event_id, failing_stream = excluded.failing_stream, " +
            "parked_at = excluded.parked_at, replay_attempts = excluded.replay_attempts, " +
            "last_attempt_at = excluded.last_attempt_at, last_attempt_error = excluded.last_attempt_error, " +
            "requested_action = excluded.requested_action, requested_at = excluded.requested_at");
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        => Task.CompletedTask;

    public OperationRole Role() => OperationRole.Events;
}

/// <summary>
/// marten#6003: delete a parked identity's row in the batch that replays or discards it, so the unpark commits with
/// the re-folded document and the shard's progression. Deleting a row that isn't there is not an error.
/// </summary>
internal class UnparkIdentityOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly EventGraph _events;
    private readonly ParkedIdentityKey _key;

    public UnparkIdentityOperation(EventGraph events, ParkedIdentityKey key)
    {
        _events = events;
        _key = key;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append($"delete from {_events.DatabaseSchemaName}.{ParkedIdentitiesTable.Name} where projection_name = ");
        builder.AppendParameter(_key.ProjectionName, NpgsqlDbType.Varchar);
        builder.Append(" and projection_version = ");
        builder.AppendParameter((int)_key.ProjectionVersion, NpgsqlDbType.Integer);
        builder.Append(" and shard_key = ");
        builder.AppendParameter(_key.ShardKey, NpgsqlDbType.Varchar);
        builder.Append(" and tenant_id = ");
        builder.AppendParameter(_key.TenantId, NpgsqlDbType.Varchar);
        builder.Append(" and identity = ");
        builder.AppendParameter(_key.IdentityText, NpgsqlDbType.Varchar);
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        => Task.CompletedTask;

    public OperationRole Role() => OperationRole.Events;
}

/// <summary>
/// marten#6003: one statement that writes a batch's slice index rows, in the batch's transaction. "On conflict do
/// nothing", because a rebuild flush may write a row a crashed earlier attempt left behind.
/// </summary>
internal class AppendSliceIndexOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly EventGraph _events;
    private readonly SliceIndexRows _rows;

    public AppendSliceIndexOperation(EventGraph events, SliceIndexRows rows)
    {
        _events = events;
        _rows = rows;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        var table = SliceIndexTable.NameFor(_rows.ProjectionName, _rows.ProjectionVersion);
        var entries = _rows.Entries;

        var tenants = new string[entries.Count];
        var sequences = new long[entries.Count];
        for (var i = 0; i < entries.Count; i++)
        {
            tenants[i] = entries[i].TenantId;
            sequences[i] = entries[i].Sequence;
        }

        builder.Append($"insert into {_events.DatabaseSchemaName}.{table} (tenant_id, identity, seq_id) select * from unnest(");
        builder.AppendParameter(tenants, NpgsqlDbType.Array | NpgsqlDbType.Varchar);
        builder.Append(", ");
        appendIdentities(builder, entries);
        builder.Append(", ");
        builder.AppendParameter(sequences, NpgsqlDbType.Array | NpgsqlDbType.Bigint);
        builder.Append(") on conflict do nothing");
    }

    private void appendIdentities(ICommandBuilder builder, IReadOnlyList<SliceIndexEntry> entries)
    {
        var type = _rows.IdentityType;
        if (type == typeof(Guid))
        {
            builder.AppendParameter(entries.Select(x => (Guid)x.Identity).ToArray(),
                NpgsqlDbType.Array | NpgsqlDbType.Uuid);
        }
        else if (type == typeof(int))
        {
            builder.AppendParameter(entries.Select(x => (int)x.Identity).ToArray(),
                NpgsqlDbType.Array | NpgsqlDbType.Integer);
        }
        else if (type == typeof(long))
        {
            builder.AppendParameter(entries.Select(x => (long)x.Identity).ToArray(),
                NpgsqlDbType.Array | NpgsqlDbType.Bigint);
        }
        else
        {
            builder.AppendParameter(entries.Select(x => (string)x.Identity).ToArray(),
                NpgsqlDbType.Array | NpgsqlDbType.Varchar);
        }
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        => Task.CompletedTask;

    public OperationRole Role() => OperationRole.Events;
}
