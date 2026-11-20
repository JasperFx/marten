#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Schema;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Storage;

/// <summary>
/// marten#6003: the parked identities and the slice index prune on <see cref="IEventDatabase" />, over
/// <see cref="ParkedIdentitiesTable" /> and <see cref="SliceIndexTable" />.
/// </summary>
public partial class MartenDatabase
{
    private const string ParkedColumns =
        "projection_name, projection_version, shard_key, tenant_id, identity, identity_type, first_failing_seq, resume_after, failure_category, exception_type, exception_message, event_type, event_id, failing_stream, parked_at, replay_attempts, last_attempt_at, last_attempt_error, requested_action, requested_at";

    private string parkedTable => $"{Options.EventGraph.DatabaseSchemaName}.{ParkedIdentitiesTable.Name}";

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParkedIdentity>> FetchParkedIdentitiesAsync(ParkedIdentityQuery query,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = new NpgsqlCommand();
        var where = new List<string>();
        if (query.ProjectionName != null)
        {
            where.Add("projection_name = :name");
            command.Parameters.AddWithValue("name", NpgsqlDbType.Varchar, query.ProjectionName);
        }

        if (query.ProjectionVersion.HasValue)
        {
            where.Add("projection_version = :version");
            command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, (int)query.ProjectionVersion.Value);
        }

        if (query.ShardKey != null)
        {
            where.Add("shard_key = :shard_key");
            command.Parameters.AddWithValue("shard_key", NpgsqlDbType.Varchar, query.ShardKey);
        }

        if (query.TenantId != null)
        {
            where.Add("tenant_id = :tenant_id");
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Varchar, query.TenantId);
        }

        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, Math.Max(query.Limit, 0));
        command.CommandText = $"select {ParkedColumns} from {parkedTable}" +
                              (where.Count > 0 ? " where " + string.Join(" and ", where) : "") +
                              " order by parked_at, first_failing_seq limit :limit";

        return await readParkedAsync(command, token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParkedShardCount>> FetchParkedCountsAsync(string? tenantId,
        CancellationToken token = default)
    {
        var command = new NpgsqlCommand(
            $"select projection_name, projection_version, shard_key, tenant_id, count(*) from {parkedTable}" +
            (tenantId == null ? "" : " where tenant_id = :tenant_id") +
            " group by projection_name, projection_version, shard_key, tenant_id" +
            " order by projection_name, projection_version, shard_key, tenant_id");

        if (tenantId != null)
        {
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Varchar, tenantId);
        }

        var counts = new List<ParkedShardCount>();
        await executeParkedReaderAsync(command, async reader =>
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                counts.Add(new ParkedShardCount(reader.GetString(0), (uint)reader.GetInt32(1), reader.GetString(2),
                    reader.GetString(3), Convert.ToInt32(reader.GetInt64(4))));
            }
        }, token).ConfigureAwait(false);

        return counts;
    }

    /// <inheritdoc />
    public async Task<int> RequestParkedActionAsync(ParkedActionRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new NpgsqlCommand();
        var where = new List<string> { "projection_name = :name" };
        command.Parameters.AddWithValue("name", NpgsqlDbType.Varchar, request.ProjectionName);

        if (request.ProjectionVersion.HasValue)
        {
            where.Add("projection_version = :version");
            command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, (int)request.ProjectionVersion.Value);
        }

        if (request.TenantId != null)
        {
            where.Add("tenant_id = :tenant_id");
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Varchar, request.TenantId);
        }

        if (request.IdentityText != null)
        {
            where.Add("identity = :identity");
            command.Parameters.AddWithValue("identity", NpgsqlDbType.Varchar, request.IdentityText);
        }

        var limit = "";
        if (request.IdentityText == null && request.Oldest is { } oldest)
        {
            limit = " limit :oldest";
            command.Parameters.AddWithValue("oldest", NpgsqlDbType.Integer, oldest);
        }

        command.Parameters.AddWithValue("action", NpgsqlDbType.Varchar, request.Action.ToString());
        command.Parameters.AddWithValue("requested_at", NpgsqlDbType.TimestampTz,
            Options.Events.TimeProvider.GetUtcNow());

        command.CommandText =
            $"with picked as (select projection_name, projection_version, shard_key, tenant_id, identity from {parkedTable} " +
            $"where {string.Join(" and ", where)} order by parked_at, first_failing_seq{limit}) " +
            $"update {parkedTable} as p set requested_action = :action, requested_at = :requested_at from picked " +
            "where p.projection_name = picked.projection_name and p.projection_version = picked.projection_version " +
            "and p.shard_key = picked.shard_key and p.tenant_id = picked.tenant_id and p.identity = picked.identity";

        var marked = 0;
        await executeParkedAsync(async conn =>
        {
            command.Connection = conn;
            marked = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, token).ConfigureAwait(false);

        return marked;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParkedIdentity>> LoadParkedIdentitiesAsync(ShardName shard,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(shard);

        var command = shardCommand(shard, "");
        command.CommandText += " order by parked_at, first_failing_seq";
        return await readParkedAsync(command, token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParkedIdentity>> LoadRequestedParkedActionsAsync(ShardName shard,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(shard);

        var command = shardCommand(shard, " and requested_action is not null");
        command.CommandText += " order by requested_at, parked_at";
        return await readParkedAsync(command, token).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task RecordParkedReplayFailureAsync(ParkedIdentityKey key, DateTimeOffset attemptedAt, string error,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var command = new NpgsqlCommand(
            $"update {parkedTable} set replay_attempts = replay_attempts + 1, last_attempt_at = :attempted_at, " +
            "last_attempt_error = :error, requested_action = null, requested_at = null " +
            "where projection_name = :name and projection_version = :version and shard_key = :shard_key " +
            "and tenant_id = :tenant_id and identity = :identity");

        command.Parameters.AddWithValue("attempted_at", NpgsqlDbType.TimestampTz, attemptedAt);
        command.Parameters.AddWithValue("error", NpgsqlDbType.Text, error);
        bindKey(command, key);

        return executeParkedAsync(async conn =>
        {
            command.Connection = conn;
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, token);
    }

    /// <inheritdoc />
    public async Task<long?> PruneSliceIndexAsync(SliceIndexPruneRequest request, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var registered = Options.EventGraph.SliceIndexTables()
            .Any(x => x.Identifier.Name == SliceIndexTable.NameFor(request.ProjectionName, request.ProjectionVersion));
        if (!registered) return null;

        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var schema = Options.EventGraph.DatabaseSchemaName;
        var table = $"{schema}.{SliceIndexTable.NameFor(request.ProjectionName, request.ProjectionVersion)}";

        // A row goes when its event is older than the window, or gone. Under per-tenant event partitioning a sequence
        // is only unique within its tenant.
        var tenantMatch = Options.Events.UseTenantPartitionedEvents ? " and e.tenant_id = i.tenant_id" : "";
        var command = new NpgsqlCommand(
            $"with deleted as (delete from {table} as i where i.seq_id <= :through" +
            (request.TenantId == null ? "" : " and i.tenant_id = :tenant_id") +
            $" and not exists (select 1 from {schema}.mt_events e where e.seq_id = i.seq_id{tenantMatch} and e.timestamp >= :older_than)" +
            " returning i.seq_id) select max(seq_id) from deleted");

        command.Parameters.AddWithValue("through", NpgsqlDbType.Bigint, request.DeleteThrough);
        command.Parameters.AddWithValue("older_than", NpgsqlDbType.TimestampTz, request.OlderThan);
        if (request.TenantId != null)
        {
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Varchar, request.TenantId);
        }

        long? highest = null;
        await executeParkedAsync(async conn =>
        {
            command.Connection = conn;
            var result = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            highest = result is null or DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }, token).ConfigureAwait(false);

        return highest;
    }

    /// <summary>
    /// marten#6003: the slice index tables in this database's event schema, registered or not: a retired projection
    /// version's table is still there until someone drops it.
    /// </summary>
    internal async Task<IReadOnlyList<string>> ExistingSliceIndexTablesAsync(NpgsqlConnection conn,
        CancellationToken token)
    {
        await using var command = conn.CreateCommand();
        command.CommandText =
            "select tablename from pg_tables where schemaname = :schema and tablename like :prefix order by tablename";
        command.Parameters.AddWithValue("schema", NpgsqlDbType.Varchar, Options.EventGraph.DatabaseSchemaName);
        command.Parameters.AddWithValue("prefix", NpgsqlDbType.Varchar, SliceIndexTable.Prefix.Replace("_", "\\_") + "%");

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    /// <summary>
    /// marten#6003: ejecting a retired projection version (or one tenant's shard of it) takes its parked rows and its
    /// slice index rows with it.
    /// </summary>
    internal async Task DeleteParkedAndSliceIndexRowsForShardAsync(string shardIdentity, CancellationToken token)
    {
        if (!ShardName.TryParse(shardIdentity, out var shard) || shard == null) return;

        var schema = Options.EventGraph.DatabaseSchemaName;
        await executeParkedAsync(async conn =>
        {
            await using (var delete = conn.CreateCommand())
            {
                delete.CommandText =
                    $"delete from {parkedTable} where projection_name = :name and projection_version = :version and shard_key = :shard_key" +
                    (shard.TenantId == null ? "" : " and tenant_id = :tenant_id");
                delete.Parameters.AddWithValue("name", NpgsqlDbType.Varchar, shard.Name);
                delete.Parameters.AddWithValue("version", NpgsqlDbType.Integer, (int)shard.Version);
                delete.Parameters.AddWithValue("shard_key", NpgsqlDbType.Varchar, shard.ShardKey);
                if (shard.TenantId != null)
                {
                    delete.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Varchar, shard.TenantId);
                }

                await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            var table = SliceIndexTable.NameFor(shard.Name, shard.Version);
            var existing = await ExistingSliceIndexTablesAsync(conn, token).ConfigureAwait(false);
            if (!existing.Contains(table)) return;

            await using var deleteIndex = conn.CreateCommand();
            deleteIndex.CommandText = $"delete from {schema}.{table}" +
                                      (shard.TenantId == null ? "" : " where tenant_id = :tenant_id");
            if (shard.TenantId != null)
            {
                deleteIndex.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Varchar, shard.TenantId);
            }

            await deleteIndex.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }

    /// <summary>
    /// marten#6003: a removed tenant's parked rows and slice index rows go with it. A re-added tenant restarts its
    /// sequence at 1, so a row left behind would point at events that are not the ones it describes.
    /// </summary>
    internal async Task DeleteParkedAndSliceIndexRowsForTenantsAsync(IReadOnlyCollection<string> tenantIds,
        CancellationToken token)
    {
        if (tenantIds.Count == 0) return;

        var schema = Options.EventGraph.DatabaseSchemaName;
        await executeParkedAsync(async conn =>
        {
            var tables = new List<string> { parkedTable };
            tables.AddRange((await ExistingSliceIndexTablesAsync(conn, token).ConfigureAwait(false))
                .Select(x => $"{schema}.{x}"));

            foreach (var table in tables)
            {
                await using var delete = conn.CreateCommand();
                delete.CommandText = $"delete from {table} where tenant_id = any(:tenants)";
                delete.Parameters.AddWithValue("tenants", NpgsqlDbType.Array | NpgsqlDbType.Varchar,
                    tenantIds.ToArray());
                await delete.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }, token).ConfigureAwait(false);
    }

    private NpgsqlCommand shardCommand(ShardName shard, string extra)
    {
        var command = new NpgsqlCommand(
            $"select {ParkedColumns} from {parkedTable} where projection_name = :name and projection_version = :version " +
            "and shard_key = :shard_key" + (shard.TenantId == null ? "" : " and tenant_id = :tenant_id") + extra);

        command.Parameters.AddWithValue("name", NpgsqlDbType.Varchar, shard.Name);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, (int)shard.Version);
        command.Parameters.AddWithValue("shard_key", NpgsqlDbType.Varchar, shard.ShardKey);
        if (shard.TenantId != null)
        {
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Varchar, shard.TenantId);
        }

        return command;
    }

    private static void bindKey(NpgsqlCommand command, ParkedIdentityKey key)
    {
        command.Parameters.AddWithValue("name", NpgsqlDbType.Varchar, key.ProjectionName);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, (int)key.ProjectionVersion);
        command.Parameters.AddWithValue("shard_key", NpgsqlDbType.Varchar, key.ShardKey);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Varchar, key.TenantId);
        command.Parameters.AddWithValue("identity", NpgsqlDbType.Varchar, key.IdentityText);
    }

    private async Task<IReadOnlyList<ParkedIdentity>> readParkedAsync(NpgsqlCommand command, CancellationToken token)
    {
        var rows = new List<ParkedIdentity>();
        await executeParkedReaderAsync(command, async reader =>
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                rows.Add(await readParkedRowAsync(reader, token).ConfigureAwait(false));
            }
        }, token).ConfigureAwait(false);

        return rows;
    }

    private static async Task<ParkedIdentity> readParkedRowAsync(DbDataReader reader, CancellationToken token)
    {
        async Task<T?> nullable<T>(int ordinal) where T : class
            => await reader.IsDBNullAsync(ordinal, token).ConfigureAwait(false)
                ? null
                : await reader.GetFieldValueAsync<T>(ordinal, token).ConfigureAwait(false);

        async Task<T?> nullableValue<T>(int ordinal) where T : struct
            => await reader.IsDBNullAsync(ordinal, token).ConfigureAwait(false)
                ? null
                : await reader.GetFieldValueAsync<T>(ordinal, token).ConfigureAwait(false);

        var identityText = reader.GetString(4);
        var identityType = reader.GetString(5);
        var requested = await nullable<string>(18).ConfigureAwait(false);

        return new ParkedIdentity
        {
            ProjectionName = reader.GetString(0),
            ProjectionVersion = (uint)reader.GetInt32(1),
            ShardKey = reader.GetString(2),
            TenantId = reader.GetString(3),
            Identity = parseIdentity(identityText, identityType),
            IdentityText = identityText,
            IdentityType = identityType,
            FirstFailingSequence = reader.GetInt64(6),
            ResumeAfter = reader.GetInt64(7),
            Category = Enum.TryParse<ShardFailureCategory>(reader.GetString(8), out var category)
                ? category
                : ShardFailureCategory.ApplyEvent,
            ExceptionType = await nullable<string>(9).ConfigureAwait(false),
            ExceptionMessage = await nullable<string>(10).ConfigureAwait(false),
            EventTypeName = await nullable<string>(11).ConfigureAwait(false),
            EventId = await nullableValue<Guid>(12).ConfigureAwait(false),
            FailingStream = await nullable<string>(13).ConfigureAwait(false),
            ParkedAt = await reader.GetFieldValueAsync<DateTimeOffset>(14, token).ConfigureAwait(false),
            ReplayAttempts = reader.GetInt32(15),
            LastAttemptAt = await nullableValue<DateTimeOffset>(16).ConfigureAwait(false),
            LastAttemptError = await nullable<string>(17).ConfigureAwait(false),
            RequestedAction = requested != null && Enum.TryParse<ParkedAction>(requested, out var action)
                ? action
                : null,
            RequestedAt = await nullableValue<DateTimeOffset>(19).ConfigureAwait(false)
        };
    }

    // The unwrapped identity in its simple type. The daemon wraps it again in the projection's own identity type.
    private static object parseIdentity(string text, string identityType) => identityType switch
    {
        "guid" => Guid.Parse(text),
        "int" => int.Parse(text, CultureInfo.InvariantCulture),
        "long" => long.Parse(text, CultureInfo.InvariantCulture),
        _ => text
    };

    private async Task executeParkedReaderAsync(NpgsqlCommand command, Func<DbDataReader, Task> read,
        CancellationToken token)
    {
        await executeParkedAsync(async conn =>
        {
            command.Connection = conn;
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            await read(reader).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }

    private async Task executeParkedAsync(Func<NpgsqlConnection, Task> action, CancellationToken token)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);
            await action(conn).ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }
}
