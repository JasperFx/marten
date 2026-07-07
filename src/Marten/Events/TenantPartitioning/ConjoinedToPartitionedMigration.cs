#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using Marten.Events.Archiving;
using Marten.Events.Daemon.HighWater;
using Marten.Storage;
using Npgsql;

namespace Marten.Events.TenantPartitioning;

/// <summary>
/// The canonical migration from a conjoined event store to one using
/// <c>Events.UseTenantPartitionedEvents</c> (marten#4682). Copies the source store's events tenant by
/// tenant into the target's per-tenant partitions using the streaming bulk import
/// (<see cref="IDocumentStore.BulkInsertEventStreamAsync(string,System.Collections.Generic.IReadOnlyList{BulkEventStreamHeader},System.Collections.Generic.IAsyncEnumerable{JasperFx.Events.IEvent},BulkEventSequenceMode,int,System.Threading.CancellationToken)"/>
/// in <see cref="BulkEventSequenceMode.PreserveSourceSequence"/> mode, marten#4879) as the engine.
///
/// <para><b>Data policy (marten#4682):</b> historical events are never renumbered. Every event keeps its
/// original <c>seq_id</c> — per-tenant gaps are expected, since the conjoined source interleaved all
/// tenants on one global sequence — so progression rows, downstream warehouses, audit logs, and any
/// external consumer that captured a sequence position stay valid. Each tenant's own
/// <c>mt_events_sequence_{suffix}</c> is advanced past its imported maximum, so the first live append
/// after migration can never collide, and each tenant's <c>HighWaterMark:{tenant}</c> progression row is
/// seeded at that maximum.</para>
///
/// <para><b>Operational shape:</b> offline-first — take source writes offline for the migration window.
/// The target is a separate schema (or database), so the source tables are never touched and rollback is
/// simply "keep using the source". Tenants are migrated one at a time, each in a single transaction, and
/// completions are recorded in the target's <c>mt_tenant_migration_log</c> table so a failed or
/// interrupted run resumes where it left off (completed tenants are skipped, the in-flight tenant — whose
/// transaction rolled back — is retried).</para>
///
/// <para>Version 1 supports single-database source and target stores (plain or
/// tenant-partitioned <see cref="DefaultTenancy"/>). For sharded targets, register tenants via the
/// sharded provisioning APIs and drive <c>BulkInsertEventStreamAsync</c> per tenant directly.</para>
/// </summary>
public class ConjoinedToPartitionedMigration
{
    /// <summary>Name of the migration-state table written to the target's event schema.</summary>
    public const string LogTableName = "mt_tenant_migration_log";

    private readonly DocumentStore _source;
    private readonly DocumentStore _target;

    public ConjoinedToPartitionedMigration(IDocumentStore source, IDocumentStore target)
    {
        _source = source.As<DocumentStore>();
        _target = target.As<DocumentStore>();

        validateStoreConfiguration();
    }

    /// <summary>Rows per COPY batch for the per-tenant event copy. Default 1000.</summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Optional subset of tenant ids to migrate. Default (null) migrates every tenant found in the
    /// source's <c>mt_events</c> / <c>mt_streams</c>.
    /// </summary>
    public IReadOnlyList<string>? TenantIds { get; set; }

    /// <summary>
    /// Phase 1 — inventory + plan (the dry-run). Reads the source's per-tenant event/stream counts and max
    /// <c>seq_id</c>s plus the target's migration log, but moves no data.
    /// </summary>
    public async Task<TenantMigrationPlan> BuildPlanAsync(CancellationToken token = default)
    {
        var sourceSchema = _source.Options.Events.DatabaseSchemaName;
        var inventory = new Dictionary<string, (long Events, long Streams, long MaxSeq)>();

        await using (var conn = _source.Tenancy.Default.Database.CreateConnection())
        {
            await conn.OpenAsync(token).ConfigureAwait(false);

            await using (var cmd = new NpgsqlCommand(
                             $"select tenant_id, count(*), max(seq_id) from {sourceSchema}.mt_events group by tenant_id",
                             conn))
            await using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    inventory[reader.GetString(0)] = (reader.GetInt64(1), 0, reader.GetInt64(2));
                }
            }

            await using (var cmd = new NpgsqlCommand(
                             $"select tenant_id, count(*) from {sourceSchema}.mt_streams group by tenant_id",
                             conn))
            await using (var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    var tenantId = reader.GetString(0);
                    var streams = reader.GetInt64(1);
                    inventory[tenantId] = inventory.TryGetValue(tenantId, out var existing)
                        ? (existing.Events, streams, existing.MaxSeq)
                        : (0, streams, 0);
                }
            }
        }

        var completed = await readCompletedTenantsAsync(token).ConfigureAwait(false);

        var tenantFilter = TenantIds?.ToHashSet();
        var items = inventory
            .Where(pair => tenantFilter == null || tenantFilter.Contains(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new TenantMigrationPlanItem(pair.Key, pair.Value.Events, pair.Value.Streams,
                pair.Value.MaxSeq, completed.Contains(pair.Key)))
            .ToList();

        return new TenantMigrationPlan(items);
    }

    /// <summary>
    /// Phase 2 — per-tenant copy. Migrates every pending tenant in the plan, one at a time, each in a
    /// single transaction, recording completions in <c>mt_tenant_migration_log</c> (so re-running after a
    /// failure resumes: completed tenants are skipped, the failed tenant is retried). Finishes by advancing
    /// the target's store-global <c>HighWaterMark</c> progression row to the migrated maximum for
    /// backward compatibility with legacy single-high-water readers.
    /// </summary>
    public async Task<TenantMigrationResult> ExecuteAsync(CancellationToken token = default)
    {
        await assertTargetIsNotTheSourceAsync(token).ConfigureAwait(false);
        await ensureLogTableAsync(token).ConfigureAwait(false);

        var plan = await BuildPlanAsync(token).ConfigureAwait(false);
        var result = new TenantMigrationResult();

        foreach (var item in plan.Tenants)
        {
            if (item.AlreadyCompleted)
            {
                result.SkippedTenants.Add(item.TenantId);
                continue;
            }

            if (item.TenantId == StorageConstants.DefaultTenantId)
            {
                throw new InvalidOperationException(
                    $"The source store carries rows under the default tenant id '{StorageConstants.DefaultTenantId}', " +
                    "which cannot own a tenant partition. Re-home those rows to a real tenant id (or exclude them " +
                    $"deliberately via {nameof(TenantIds)}) before migrating.");
            }

            await migrateTenantAsync(item, token).ConfigureAwait(false);

            result.MigratedTenants.Add(item.TenantId);
            result.EventsCopied += item.EventCount;
        }

        // Phase 3 (marten#4682): keep the store-global HighWaterMark row moving for anything that still
        // reads the legacy single high water. Under per-tenant partitioning the detector itself works off
        // the per-tenant rows / max(seq_id), so this is purely backward compatibility.
        if (result.MigratedTenants.Count > 0)
        {
            var globalMax = plan.Tenants
                .Where(x => result.MigratedTenants.Contains(x.TenantId))
                .Max(x => x.MaxSequence);
            await upsertStoreGlobalHighWaterAsync(globalMax, token).ConfigureAwait(false);
        }

        return result;
    }

    private async Task migrateTenantAsync(TenantMigrationPlanItem item, CancellationToken token)
    {
        // Log first (completed = null): a crash mid-tenant leaves a visible in-flight row, and the
        // re-run's plan still treats the tenant as pending because only completed rows are skipped.
        await markTenantStartedAsync(item, token).ConfigureAwait(false);

        // Idempotent: provisions the tenant's partitions + its own mt_events_sequence_{suffix} on the target.
        await _target.Advanced.AddMartenManagedTenantsAsync(token, item.TenantId).ConfigureAwait(false);

        var headers = await readStreamHeadersAsync(item.TenantId, token).ConfigureAwait(false);

        // The engine (marten#4879): stream the tenant's events out of the source in their original global
        // order and copy them into the target's tenant partition with their seq_ids preserved. Reading
        // through QueryAllRawEvents applies the source store's upcasters/serialization exactly like any
        // other read, and MaybeArchived() keeps archived events in the copy. One transaction per tenant —
        // a failure rolls the whole tenant back for a clean retry.
        await using (var session = _source.QuerySession(item.TenantId))
        {
            var orderedEvents = session.Events.QueryAllRawEvents()
                .Where(x => x.MaybeArchived())
                .OrderBy(x => x.Sequence)
                .ToAsyncEnumerable(token);

            await _target.BulkInsertEventStreamAsync(item.TenantId, headers, orderedEvents,
                BulkEventSequenceMode.PreserveSourceSequence, BatchSize, token).ConfigureAwait(false);
        }

        await verifyTenantCopyAsync(item, token).ConfigureAwait(false);
        await markTenantCompletedAsync(item.TenantId, token).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<BulkEventStreamHeader>> readStreamHeadersAsync(
        string tenantId, CancellationToken token)
    {
        var sourceSchema = _source.Options.Events.DatabaseSchemaName;
        var asGuid = _source.Options.Events.StreamIdentity == StreamIdentity.AsGuid;
        var headers = new List<BulkEventStreamHeader>();

        await using var conn = _source.Tenancy.Default.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            $"select id, type, version, is_archived from {sourceSchema}.mt_streams where tenant_id = @tenant",
            conn);
        cmd.Parameters.AddWithValue("tenant", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            headers.Add(new BulkEventStreamHeader
            {
                Id = asGuid ? reader.GetGuid(0) : default,
                Key = asGuid ? null : reader.GetString(0),
                AggregateTypeName = await reader.IsDBNullAsync(1, token).ConfigureAwait(false)
                    ? null
                    : reader.GetString(1),
                Version = await reader.IsDBNullAsync(2, token).ConfigureAwait(false) ? 0 : reader.GetInt64(2),
                IsArchived = !await reader.IsDBNullAsync(3, token).ConfigureAwait(false) && reader.GetBoolean(3)
            });
        }

        return headers;
    }

    private async Task verifyTenantCopyAsync(TenantMigrationPlanItem item, CancellationToken token)
    {
        var targetSchema = _target.Options.Events.DatabaseSchemaName;

        await using var conn = _target.Tenancy.Default.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            $"select count(*) from {targetSchema}.mt_events where tenant_id = @tenant", conn);
        cmd.Parameters.AddWithValue("tenant", item.TenantId);

        var copied = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
        if (copied != item.EventCount)
        {
            throw new InvalidOperationException(
                $"Tenant '{item.TenantId}' verification failed: the target holds {copied} events but the " +
                $"Phase 1 inventory counted {item.EventCount}. Was the source written to during the migration " +
                "window? The tenant is NOT marked completed; re-running the migration will retry it.");
        }
    }

    private void validateStoreConfiguration()
    {
        if (_source.Options.Events.TenancyStyle != TenancyStyle.Conjoined)
        {
            throw new InvalidOperationException(
                "The source store must use TenancyStyle.Conjoined for its events — that is the starting " +
                "state this migration moves away from.");
        }

        if (_source.Options.Events.UseTenantPartitionedEvents)
        {
            throw new InvalidOperationException(
                "The source store already uses per-tenant partitioned events. There is nothing to migrate.");
        }

        if (_target.Options.Events.TenancyStyle != TenancyStyle.Conjoined ||
            !_target.Options.Events.UseTenantPartitionedEvents)
        {
            throw new InvalidOperationException(
                "The target store must be configured with Events.TenancyStyle = TenancyStyle.Conjoined and " +
                "Events.UseTenantPartitionedEvents = true.");
        }

        if (_source.Options.Events.StreamIdentity != _target.Options.Events.StreamIdentity)
        {
            throw new InvalidOperationException(
                "The source and target stores must use the same StreamIdentity.");
        }
    }

    private async Task assertTargetIsNotTheSourceAsync(CancellationToken token)
    {
        if (_source.Tenancy is not DefaultTenancy || _target.Tenancy is not DefaultTenancy)
        {
            throw new InvalidOperationException(
                "Version 1 of the conjoined → partitioned migration supports single-database source and " +
                "target stores. For sharded targets, provision tenants per shard and drive " +
                $"{nameof(IDocumentStore.BulkInsertEventStreamAsync)} directly.");
        }

        if (_source.Options.Events.DatabaseSchemaName != _target.Options.Events.DatabaseSchemaName)
        {
            return;
        }

        await using var sourceConn = _source.Tenancy.Default.Database.CreateConnection();
        await using var targetConn = _target.Tenancy.Default.Database.CreateConnection();
        var source = new NpgsqlConnectionStringBuilder(sourceConn.ConnectionString);
        var target = new NpgsqlConnectionStringBuilder(targetConn.ConnectionString);

        if (string.Equals(source.Host, target.Host, StringComparison.OrdinalIgnoreCase)
            && source.Port == target.Port
            && string.Equals(source.Database, target.Database, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The target store points at the same database AND the same event schema as the source. " +
                "The migration copies side-by-side (the source tables stay untouched for rollback), so the " +
                "target must use a different schema or database.");
        }
    }

    private async Task ensureLogTableAsync(CancellationToken token)
    {
        var targetSchema = _target.Options.Events.DatabaseSchemaName;

        await using var conn = _target.Tenancy.Default.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand($@"
create table if not exists {targetSchema}.{LogTableName} (
    tenant_id varchar not null primary key,
    source_max_seq_id bigint not null,
    event_count bigint not null,
    stream_count bigint not null,
    started timestamp with time zone not null default (transaction_timestamp()),
    completed timestamp with time zone
)", conn);
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private async Task<HashSet<string>> readCompletedTenantsAsync(CancellationToken token)
    {
        var targetSchema = _target.Options.Events.DatabaseSchemaName;
        var completed = new HashSet<string>();

        await using var conn = _target.Tenancy.Default.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        // to_regclass: the log table only exists once a migration has run; a fresh dry-run must not create it.
        await using (var existsCmd = new NpgsqlCommand("select to_regclass(@name) is not null", conn))
        {
            existsCmd.Parameters.AddWithValue("name", $"{targetSchema}.{LogTableName}");
            var exists = (bool)(await existsCmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            if (!exists)
            {
                return completed;
            }
        }

        await using var cmd = new NpgsqlCommand(
            $"select tenant_id from {targetSchema}.{LogTableName} where completed is not null", conn);
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            completed.Add(reader.GetString(0));
        }

        return completed;
    }

    private async Task markTenantStartedAsync(TenantMigrationPlanItem item, CancellationToken token)
    {
        var targetSchema = _target.Options.Events.DatabaseSchemaName;

        await using var conn = _target.Tenancy.Default.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand($@"
insert into {targetSchema}.{LogTableName} (tenant_id, source_max_seq_id, event_count, stream_count)
values (@tenant, @max, @events, @streams)
on conflict (tenant_id) do update
    set source_max_seq_id = excluded.source_max_seq_id,
        event_count = excluded.event_count,
        stream_count = excluded.stream_count,
        started = transaction_timestamp(),
        completed = null", conn);
        cmd.Parameters.AddWithValue("tenant", item.TenantId);
        cmd.Parameters.AddWithValue("max", item.MaxSequence);
        cmd.Parameters.AddWithValue("events", item.EventCount);
        cmd.Parameters.AddWithValue("streams", item.StreamCount);
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private async Task markTenantCompletedAsync(string tenantId, CancellationToken token)
    {
        var targetSchema = _target.Options.Events.DatabaseSchemaName;

        await using var conn = _target.Tenancy.Default.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        await using var cmd = new NpgsqlCommand(
            $"update {targetSchema}.{LogTableName} set completed = transaction_timestamp() where tenant_id = @tenant",
            conn);
        cmd.Parameters.AddWithValue("tenant", tenantId);
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private async Task upsertStoreGlobalHighWaterAsync(long seq, CancellationToken token)
    {
        var targetSchema = _target.Options.Events.DatabaseSchemaName;

        await using var conn = _target.Tenancy.Default.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        // #4681: the literal identity comes from HighWaterShardIdentity, never hand-rolled.
        await using var cmd = new NpgsqlCommand($@"
INSERT INTO {targetSchema}.mt_event_progression (name, last_seq_id)
VALUES ('{HighWaterShardIdentity.StoreGlobal}', @seq)
ON CONFLICT (name) DO UPDATE SET last_seq_id = GREATEST({targetSchema}.mt_event_progression.last_seq_id, @seq)",
            conn);
        cmd.Parameters.AddWithValue("seq", seq);
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }
}
