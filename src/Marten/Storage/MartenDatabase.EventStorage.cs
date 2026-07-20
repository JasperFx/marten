#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Daemon.Internals;
using Marten.Events.Daemon.Progress;
using Marten.Internal.Sessions;
using Marten.Linq.QueryHandlers;
using Marten.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Storage;

public partial class MartenDatabase : IEventDatabase
{
    private string _storageIdentifier;

    public async Task MarkEventsAsSkipped(long[] sequences, CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);
            await conn.CreateCommand(
                    $"update {Options.EventGraph.DatabaseSchemaName}.mt_events set is_skipped = TRUE where seq_id = ANY(:sequences)")
                .With("sequences", sequences)
                .ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Persist the extended progression telemetry (heartbeat / agent_status / pause_reason /
    ///     running_on_node) the async daemon publishes for a shard, via the
    ///     <c>mt_mark_event_progression_extended</c> function. Driven by the JasperFx.Events
    ///     <c>ExtendedProgressionWriter</c> observer on status transitions and throttled heartbeats
    ///     (CritterWatch #750). The function updates only the telemetry columns on an existing row,
    ///     so it never rolls back committed <c>last_seq_id</c>.
    /// </summary>
    public async Task WriteExtendedProgressionAsync(ShardState state, CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);
            await conn.CreateCommand(
                    $"select {Options.EventGraph.DatabaseSchemaName}.mt_mark_event_progression_extended(:name, :seq, :heartbeat, :status, :reason, :node)")
                .With("name", state.ShardName, NpgsqlDbType.Varchar)
                .With("seq", state.Sequence, NpgsqlDbType.Bigint)
                .With("heartbeat", (object?)state.LastHeartbeat ?? DBNull.Value, NpgsqlDbType.TimestampTz)
                .With("status", (object?)state.AgentStatus ?? DBNull.Value, NpgsqlDbType.Varchar)
                .With("reason", (object?)state.PauseReason ?? DBNull.Value, NpgsqlDbType.Text)
                .With("node", (object?)state.RunningOnNode ?? DBNull.Value, NpgsqlDbType.Integer)
                .ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    public async Task<long?> FindEventStoreFloorAtTimeAsync(DateTimeOffset timestamp, CancellationToken token)
    {
        var sql =
            $"select seq_id from {Options.Events.DatabaseSchemaName}.mt_events where timestamp >= :timestamp order by seq_id limit 1";
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await using var conn = CreateConnection();

        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);
            var raw = await conn.CreateCommand(sql).With("timestamp", timestamp.ToUniversalTime(), NpgsqlDbType.TimestampTz).ExecuteScalarAsync(token).ConfigureAwait(false);
            return raw is DBNull ? null : (long?)raw;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// #4596 Phase 1 Session 4 — override the jasperfx#407 default-throwing
    /// per-tenant overload. Non-null <paramref name="tenantId"/> adds an
    /// <c>AND tenant_id = :tenantId</c> filter to the events scan so the
    /// returned floor is the earliest sequence at or after the timestamp
    /// <em>belonging to that tenant</em> — Phase 1 partitioned mt_events by
    /// tenant_id so the planner only touches that tenant's partition. Null
    /// preserves the store-global behavior (today's tenantless overload).
    /// </summary>
    public async Task<long?> FindEventStoreFloorAtTimeAsync(DateTimeOffset timestamp, string? tenantId, CancellationToken token)
    {
        if (tenantId == null)
        {
            return await FindEventStoreFloorAtTimeAsync(timestamp, token).ConfigureAwait(false);
        }

        var sql =
            $"select seq_id from {Options.Events.DatabaseSchemaName}.mt_events where timestamp >= :timestamp and tenant_id = :tenant_id order by seq_id limit 1";
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);
            var raw = await conn
                .CreateCommand(sql)
                .With("timestamp", timestamp.ToUniversalTime(), NpgsqlDbType.TimestampTz)
                .With("tenant_id", tenantId, NpgsqlDbType.Varchar)
                .ExecuteScalarAsync(token).ConfigureAwait(false);
            return raw is null or DBNull ? null : (long?)raw;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    string IEventDatabase.StorageIdentifier => _storageIdentifier;

    /// <summary>
    /// Fetch the highest assigned event sequence number in this database
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<long> FetchHighestEventSequenceNumber(CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);
        await using var conn = CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        try
        {
            // #4705: under per-tenant event partitioning the store-global mt_events_sequence is
            // never advanced -- every tenant's events draw seq_id from its own
            // mt_events_sequence_{suffix}, so the global sequence's last_value is stale (reads as 1).
            // Callers use this as a high-water ceiling (e.g. the composite single-pass replay
            // executor); reading the stale 1 made composite shards replay only events 0..1 and
            // stall. Read the real maximum from the events table instead in that mode.
            var sql = Options.Events.UseTenantPartitionedEvents
                ? $"select coalesce(max(seq_id), 0) from {Options.Events.DatabaseSchemaName}.mt_events;"
                : $"select last_value from {Options.Events.DatabaseSchemaName}.mt_events_sequence;";

            var highest = (long)(await conn
                .CreateCommand(sql)
                .ExecuteScalarAsync(token).ConfigureAwait(false))!;

            return highest;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Fetch the highest <c>seq_id</c> persisted in this database's <c>mt_events</c> table —
    ///     the absolute physical maximum, distinct from the HighWaterMark (max-safe-to-read).
    ///     Returns null when the table is empty.
    /// </summary>
    public async Task<long?> FetchMaxEventSequenceAsync(CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);
        await using var conn = CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        try
        {
            var raw = await conn
                .CreateCommand($"select max(seq_id) from {Options.Events.DatabaseSchemaName}.mt_events;")
                .ExecuteScalarAsync(token).ConfigureAwait(false);

            return raw is null or DBNull ? null : (long?)raw;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }


    /// <summary>
    ///     Fetch the current size of the event store tables, including the current value
    ///     of the event sequence number
    /// </summary>
    /// <param name="tenantId">
    ///     Specify the database containing this tenant id. If omitted, this method uses the default
    ///     database
    /// </param>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<EventStoreStatistics> FetchEventStoreStatistics(
        CancellationToken token = default)
    {
        // #4705: under per-tenant partitioning the store-global mt_events_sequence is never advanced,
        // so EventSequenceNumber would be stale (reads as 1). Read max(seq_id) in that mode so it stays
        // consistent with FetchHighestEventSequenceNumber / FetchMaxEventSequenceAsync.
        var highWaterSql = Options.Events.UseTenantPartitionedEvents
            ? $"select coalesce(max(seq_id), 0) from {Options.Events.DatabaseSchemaName}.mt_events;"
            : $"select last_value from {Options.Events.DatabaseSchemaName}.mt_events_sequence;";

        var sql = $@"
select count(*) from {Options.Events.DatabaseSchemaName}.mt_events;
select count(*) from {Options.Events.DatabaseSchemaName}.mt_streams;
{highWaterSql}
";

        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var statistics = new EventStoreStatistics();

        await using var conn = CreateConnection();

        await conn.OpenAsync(token).ConfigureAwait(false);

        try
        {
            await using var reader = await conn.CreateCommand(sql).ExecuteReaderAsync(token).ConfigureAwait(false);

            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                statistics.EventCount = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            }

            await reader.NextResultAsync(token).ConfigureAwait(false);

            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                statistics.StreamCount = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            }

            await reader.NextResultAsync(token).ConfigureAwait(false);

            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                statistics.EventSequenceNumber = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            }

            return statistics;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }


    /// <summary>
    ///     Check the current progress of all asynchronous projections
    ///     within this database
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<IReadOnlyList<ShardState>> AllProjectionProgress(
        CancellationToken token = default)
    {
        return await AllProjectionProgress(tenantId: null, token).ConfigureAwait(false);
    }

    /// <summary>
    /// #4596 Phase 1 Session 4 — override the jasperfx#407 default-throwing
    /// per-tenant overload. Non-null <paramref name="tenantId"/> filters the
    /// progression rows by the trailing tenant suffix on
    /// <see cref="ShardName.Identity"/> (the
    /// <c>{Name}:{ShardKey}:{tenantId}</c> 3-segment grammar). Null preserves
    /// the today's-behavior "every row" semantics.
    /// </summary>
    public async Task<IReadOnlyList<ShardState>> AllProjectionProgress(string? tenantId, CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var statement = new ProjectionProgressStatement(Options.EventGraph) { TenantId = tenantId };
        var handler = (IQueryHandler<IReadOnlyList<ShardState>>)new ListQueryHandler<ShardState>(
            statement,
            new ShardStateSelector(Options.EventGraph));

        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);

            var builder = new CommandBuilder();
            handler.ConfigureCommand(builder, null);

            await using var reader = await conn.ExecuteReaderAsync(builder, token).ConfigureAwait(false);
            return await handler.HandleAsync(reader, null, token).ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// #4962 / jasperfx#435 — targeted per-cell progression read for a single (projection, tenant).
    /// The <c>mt_event_progression.name</c> column holds a full <see cref="ShardName.Identity"/>, so a
    /// projection name can map to several rows (blue/green versions, custom shard keys, per-tenant
    /// partitions). Resolution:
    /// <list type="bullet">
    /// <item>candidates are the rows whose parsed <see cref="ShardName.Name"/> equals
    /// <paramref name="projectionName"/> and whose parsed <see cref="ShardName.TenantId"/> equals
    /// <paramref name="tenantId"/> — a null <paramref name="tenantId"/> matches only the bare
    /// store-global rows (no tenant suffix);</item>
    /// <item>when several match (a blue/green deploy), the NEWEST version wins, tie-broken on the highest
    /// sequence;</item>
    /// <item>no match returns null.</item>
    /// </list>
    /// <see cref="ProjectionProgressRow.AgentStatus"/> and <see cref="ProjectionProgressRow.LastHeartbeat"/>
    /// are always null: Marten models the columns but no daemon path writes them (jasperfx#519).
    /// </summary>
    public async ValueTask<ProjectionProgressRow?> ReadProjectionProgressAsync(
        string projectionName, string? tenantId, CancellationToken token)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);

            var builder = new CommandBuilder();
            // The trailing ':' guards against a projection whose name is a prefix of another
            // (e.g. "Orders" must not match "OrdersHistory:All").
            builder.Append(
                $"select name, last_seq_id from {Options.EventGraph.DatabaseSchemaName}.mt_event_progression where name like ");
            builder.AppendParameter(projectionName + ":%");

            ShardName? best = null;
            var bestSequence = 0L;

            await using var reader = await conn.ExecuteReaderAsync(builder, token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var name = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
                var sequence = await reader.GetFieldValueAsync<long>(1, token).ConfigureAwait(false);

                if (!ShardName.TryParse(name, out var shard) || shard is null)
                {
                    continue;
                }

                if (shard.Name != projectionName || shard.TenantId != tenantId)
                {
                    continue;
                }

                // Newest version wins; tie-break on the furthest-along sequence.
                if (best is null || shard.Version > best.Version ||
                    (shard.Version == best.Version && sequence > bestSequence))
                {
                    best = shard;
                    bestSequence = sequence;
                }
            }

            return best is null ? null : new ProjectionProgressRow(projectionName, tenantId, bestSequence, null, null);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// #4975 / jasperfx#529 — exact per-cell progression read. Unlike the
    /// <see cref="ReadProjectionProgressAsync(string,string?,CancellationToken)"/> overload this does no
    /// version/shard collapsing: it looks up the single <c>mt_event_progression</c> row whose <c>name</c>
    /// equals <paramref name="name"/>'s <see cref="ShardName.Identity"/> verbatim, so a blue/green deploy's
    /// versions, a sliced projection's shard keys, and per-tenant partitions each address their own row.
    /// A <see cref="ShardName.ShardKey"/> of <c>All</c> is the projection's global cell. Returns null when no
    /// row exists for that identity; <see cref="ProjectionProgressRow.AgentStatus"/> and
    /// <see cref="ProjectionProgressRow.LastHeartbeat"/> stay null (jasperfx#519).
    /// </summary>
    public async ValueTask<ProjectionProgressRow?> ReadProjectionProgressAsync(
        ShardName name, CancellationToken token)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);

            var builder = new CommandBuilder();
            builder.Append(
                $"select last_seq_id from {Options.EventGraph.DatabaseSchemaName}.mt_event_progression where name = ");
            builder.AppendParameter(name.Identity);

            await using var reader = await conn.ExecuteReaderAsync(builder, token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                return null;
            }

            var sequence = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            return new ProjectionProgressRow(name.Name, name.TenantId, sequence, null, null);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// #4785 / jasperfx#473 — Marten override of the store-agnostic
    /// <see cref="IEventDatabase.DeleteProjectionProgressByShardNameAsync"/>
    /// (default impl throws). Drops the single <c>mt_event_progression</c> row
    /// whose <c>name</c> matches the raw <see cref="ShardName.Identity"/> verbatim
    /// — bypassing the registered-projection lookup that the
    /// <c>IEventStore&lt;,&gt;.DeleteProjectionProgressAsync</c> path goes through
    /// (and that throws <see cref="ArgumentOutOfRangeException"/> for an
    /// unregistered name). This is the eject path for an orphan shard whose
    /// projection has been renamed/versioned/removed since the row was written.
    /// A non-existent identity is a clean no-op (zero rows affected, no throw).
    /// Per-tenant scoping flows through the identity itself: a
    /// <c>{Name}:{ShardKey}:{tenantId}</c> identity deletes only that tenant's row.
    /// </summary>
    public async Task DeleteProjectionProgressByShardNameAsync(string shardIdentity, CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var sessionOptions = SessionOptions.ForDatabase(this);
        sessionOptions.AllowAnyTenant = true;
        await using var session = Options.EventGraph.Store.LightweightSession(sessionOptions);

        session.QueueOperation(new DeleteProjectionProgress(Options.EventGraph, shardIdentity));

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    ///     marten#4546 / jasperfx#356: count the stored projection/subscription dead letter
    ///     events for a single shard. With SkipApplyErrors on (the JasperFx.Events 2.0 default)
    ///     a failed Apply is recorded as a <see cref="DeadLetterEvent" />, so the accumulation is
    ///     the primary "this projection is unhealthy" signal. DeadLetterEvent is a single-tenanted
    ///     document in the event store schema, so this count spans every tenant sharing this
    ///     database (consistent with <see cref="AllProjectionProgress" />).
    /// </summary>
    public async Task<long> CountDeadLetterEventsAsync(ShardName shard, CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(DeadLetterEvent), token).ConfigureAwait(false);

        // DeadLetterEvent is a Marten document, so query it with LINQ — the JSONB
        // member paths (and serializer Casing) are handled by Marten.
        await using var session = Options.EventGraph.Store.QuerySession(SessionOptions.ForDatabase(this));
        return await session.Query<DeadLetterEvent>()
            .Where(x => x.ProjectionName == shard.Name && x.ShardName == shard.ShardKey)
            .CountAsync(token).ConfigureAwait(false);
    }

    /// <summary>
    ///     marten#4546 / jasperfx#356: fetch the stored dead letter event counts for this database,
    ///     one row per shard (<see cref="DeadLetterShardCount.ProjectionName" /> +
    ///     <see cref="DeadLetterShardCount.ShardKey" />). Mirrors the "give me every row" shape of
    ///     <see cref="AllProjectionProgress" />.
    /// </summary>
    public async Task<IReadOnlyList<DeadLetterShardCount>> FetchDeadLetterCountsAsync(CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(DeadLetterEvent), token).ConfigureAwait(false);

        await using var session = Options.EventGraph.Store.QuerySession(SessionOptions.ForDatabase(this));
        var rows = await session.Query<DeadLetterEvent>()
            .GroupBy(x => new { x.ProjectionName, x.ShardName })
            .Select(g => new { g.Key.ProjectionName, g.Key.ShardName, Count = g.Count() })
            .ToListAsync(token).ConfigureAwait(false);

        return rows
            .Select(x => new DeadLetterShardCount(x.ProjectionName, x.ShardName, x.Count))
            .ToList();
    }

    /// <summary>
    ///     Per-tenant dead-letter counts (CritterWatch#381 / jasperfx#450). Under
    ///     <c>UseTenantPartitionedEvents</c> the dead-letter table stays store-global, but each row
    ///     records the failing event's <see cref="DeadLetterEvent.TenantId" />, so the counts that
    ///     would otherwise collide on <c>{ProjectionName}:{ShardName}</c> are separated per tenant.
    ///     A null <paramref name="tenantId" /> falls back to the store-global (tenant-collapsed) shape.
    /// </summary>
    public async Task<IReadOnlyList<DeadLetterShardCount>> FetchDeadLetterCountsAsync(string? tenantId,
        CancellationToken token = default)
    {
        if (tenantId == null)
        {
            return await FetchDeadLetterCountsAsync(token).ConfigureAwait(false);
        }

        await EnsureStorageExistsAsync(typeof(DeadLetterEvent), token).ConfigureAwait(false);

        await using var session = Options.EventGraph.Store.QuerySession(SessionOptions.ForDatabase(this));
        var rows = await session.Query<DeadLetterEvent>()
            .Where(x => x.TenantId == tenantId)
            .GroupBy(x => new { x.ProjectionName, x.ShardName })
            .Select(g => new { g.Key.ProjectionName, g.Key.ShardName, Count = g.Count() })
            .ToListAsync(token).ConfigureAwait(false);

        return rows
            .Select(x => new DeadLetterShardCount(x.ProjectionName, x.ShardName, x.Count, tenantId))
            .ToList();
    }

    /// <summary>
    ///     CritterWatch#369: fetch the stored dead-letter event rows for a single shard — the drill-in
    ///     companion to <see cref="CountDeadLetterEventsAsync" />. Most recent failures first (by event
    ///     sequence), paged. A null <paramref name="tenantId" /> spans every tenant sharing this database;
    ///     under <c>UseTenantPartitionedEvents</c> pass a tenant to scope to one partition.
    /// </summary>
    public async Task<IReadOnlyList<DeadLetterEvent>> QueryDeadLetterEventsAsync(ShardName shard,
        string? tenantId, int offset, int limit, CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(DeadLetterEvent), token).ConfigureAwait(false);

        await using var session = Options.EventGraph.Store.QuerySession(SessionOptions.ForDatabase(this));
        var query = session.Query<DeadLetterEvent>()
            .Where(x => x.ProjectionName == shard.Name && x.ShardName == shard.ShardKey);

        if (tenantId != null)
        {
            query = query.Where(x => x.TenantId == tenantId);
        }

        return await query
            .OrderByDescending(x => x.EventSequence)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ShardState>> FetchProjectionProgressFor(ShardName[] names, CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var handler = (IQueryHandler<IReadOnlyList<ShardState>>)new ListQueryHandler<ShardState>(
            new ProjectionProgressStatement(Options.EventGraph){Names = names},
            new ShardStateSelector(Options.EventGraph));

        await using var conn = CreateConnection();
        try
        {
            await conn.OpenAsync(token).ConfigureAwait(false);

            var builder = new CommandBuilder();
            handler.ConfigureCommand(builder, null);

            await using var reader = await conn.ExecuteReaderAsync(builder, token).ConfigureAwait(false);
            return await handler.HandleAsync(reader, null, token).ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    Task IEventDatabase.WaitForNonStaleProjectionDataAsync(TimeSpan timeout)
    {
        return this.WaitForNonStaleProjectionDataAsync(timeout);
    }

    /// <summary>
    ///     Check the current progress of a single projection or projection shard
    ///     within this database
    /// </summary>
    /// <param name="name"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task<long> ProjectionProgressFor(ShardName name,
        CancellationToken token = default)
    {
        await EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var statement = new ProjectionProgressStatement(Options.EventGraph) { Name = name };

        var handler = new OneResultHandler<ShardState>(statement,
            new ShardStateSelector(Options.EventGraph), true, false);

        await using var conn = CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        try
        {
            var builder = new CommandBuilder();
            handler.ConfigureCommand(builder, null);

            await using var reader = await conn.ExecuteReaderAsync(builder, token).ConfigureAwait(false);
            var state = await handler.HandleAsync(reader, null, token).ConfigureAwait(false);

            return state?.Sequence ?? 0;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    public Uri DatabaseUri => Describe().DatabaseUri();

    /// <summary>
    ///     *If* a projection daemon has been started for this database, this
    ///     is the ShardStateTracker for the running daemon. This is useful in testing
    ///     scenarios
    /// </summary>
    public ShardStateTracker Tracker { get; private set; }

    async Task IEventDatabase.StoreDeadLetterEventAsync(object storage, DeadLetterEvent deadLetterEvent,
        CancellationToken token)
    {
        try
        {
            using var session = storage.As<DocumentStore>().LightweightSession(SessionOptions.ForDatabase(this));
            session.Store(deadLetterEvent);
            await session.SaveChangesAsync(token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // TODO -- something to log this?
        }
    }

    Task IEventDatabase.EnsureStorageExistsAsync(Type storageType, CancellationToken token)
    {
        return EnsureStorageExistsAsync(storageType, token).AsTask();
    }

    internal IProjectionDaemon StartProjectionDaemon(DocumentStore store, ILogger? logger = null)
    {
        logger ??= store.Options.LogFactory?.CreateLogger<ProjectionDaemon>() ??
                   store.Options.DotNetLogger ?? NullLogger.Instance;

        if (Options.EventGraph.UseListenNotifyForEventAppends)
        {
            store.Options.Projections.Wakeup =
                new PostgresqlListenWakeup(DataSource, logger);
        }

        var detector = new HighWaterDetector(this, Options.EventGraph, logger);

        return new ProjectionDaemon(store, this, logger, detector);
    }
}
