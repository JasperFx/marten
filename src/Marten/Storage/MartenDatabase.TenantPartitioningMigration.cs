#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Schema;
using Marten.Events.TenantPartitioning;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Storage;

/// <summary>
/// #4682 — conjoined → <c>UseTenantPartitionedEvents</c> migration, Phase 1 (Prepare).
/// Builds a read-only <see cref="TenantPartitioningMigrationPlan"/>: validates the prerequisites,
/// inventories per-tenant event counts and high-water sequence values, and audits the existing
/// <c>mt_event_progression</c> rows. This is the <c>--dry-run</c> path — it touches no data.
/// Phases 2 (per-tenant copy + attach) and 3 (swap + cleanup) build on the plan this produces.
/// </summary>
public partial class MartenDatabase
{
    /// <summary>
    /// Inventory and validate this (conjoined) event store for migration to
    /// <c>Events.UseTenantPartitionedEvents</c>. Read-only: no schema or data changes are made.
    /// Inspect <see cref="TenantPartitioningMigrationPlan.CanProceed"/> and
    /// <see cref="TenantPartitioningMigrationPlan.Errors"/> before running the data-movement phases.
    /// </summary>
    public async Task<TenantPartitioningMigrationPlan> CreateTenantPartitioningMigrationPlanAsync(
        CancellationToken token = default)
    {
        var plan = new TenantPartitioningMigrationPlan();
        var events = Options.Events;
        var schema = events.DatabaseSchemaName;

        // --- Static prerequisite validation (no DB access needed) ---
        if (events.TenancyStyle != TenancyStyle.Conjoined)
        {
            plan.Errors.Add(
                $"The source event store must use TenancyStyle.Conjoined to migrate to tenant partitioning; found {events.TenancyStyle}.");
        }

        if (!events.UseTenantPartitionedEvents)
        {
            plan.Errors.Add(
                "The target configuration must set Events.UseTenantPartitionedEvents = true. Configure the store for partitioning, then build the migration plan against it.");
        }

        await using var conn = CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        try
        {
            var eventsTable = $"{schema}.mt_events";
            if (await regclassAsync(conn, eventsTable, token).ConfigureAwait(false) == null)
            {
                plan.Errors.Add($"No {eventsTable} table found — there is no conjoined event store to migrate.");
                return plan;
            }

            // Every event must carry a tenant_id; a null tenant cannot be routed to a partition.
            plan.EventsWithoutTenant = await countAsync(conn,
                $"select count(*) from {eventsTable} where tenant_id is null", token).ConfigureAwait(false);
            if (plan.EventsWithoutTenant > 0)
            {
                plan.Errors.Add(
                    $"{plan.EventsWithoutTenant} event row(s) have a null tenant_id and cannot be assigned to a tenant partition. Backfill tenant_id before migrating.");
            }

            // Registered partitions (tenant_id -> partition_suffix), if the lookup table exists yet.
            var registeredSuffixes = await loadRegisteredPartitionsAsync(conn, token).ConfigureAwait(false);

            await loadTenantInventoryAsync(conn, eventsTable, registeredSuffixes, plan, token).ConfigureAwait(false);
            await auditProgressionRowsAsync(conn, schema, plan, token).ConfigureAwait(false);

            foreach (var tenant in plan.TenantsMissingPartitions)
            {
                plan.Warnings.Add(
                    $"Tenant '{tenant.TenantId}' has {tenant.EventCount} event(s) but no registered partition. Register it via AddMartenManagedTenantsAsync before Phase 2 (or the tool can auto-populate from distinct tenant ids).");
            }

            foreach (var straggler in plan.UnrecognizedProgressionRows)
            {
                plan.Warnings.Add(
                    $"mt_event_progression row '{straggler.Name}' does not match any known shard or high-water identity. Review it before the table swap.");
            }
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }

        return plan;
    }

    private async Task<Dictionary<string, string>> loadRegisteredPartitionsAsync(
        NpgsqlConnection conn, CancellationToken token)
    {
        var result = new Dictionary<string, string>();
        var tenantPartitions = Options.TenantPartitions;
        if (tenantPartitions == null)
        {
            return result;
        }

        var tenantsTable = tenantPartitions.TenantsTableName.QualifiedName;
        if (await regclassAsync(conn, tenantsTable, token).ConfigureAwait(false) == null)
        {
            return result;
        }

        await using var cmd = conn.CreateCommand(
            $"select partition_value, partition_suffix from {tenantsTable}");
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    private static async Task loadTenantInventoryAsync(NpgsqlConnection conn, string eventsTable,
        IReadOnlyDictionary<string, string> registeredSuffixes,
        TenantPartitioningMigrationPlan plan, CancellationToken token)
    {
        await using var cmd = conn.CreateCommand(
            $"select tenant_id, count(*), max(seq_id) from {eventsTable} where tenant_id is not null group by tenant_id order by tenant_id");
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var tenantId = reader.GetString(0);
            var count = reader.GetInt64(1);
            var maxSeq = reader.GetInt64(2);
            var registered = registeredSuffixes.TryGetValue(tenantId, out var suffix);
            plan.Tenants.Add(new TenantEventInventory(tenantId, count, maxSeq, registered, registered ? suffix : null));
        }
    }

    private static async Task auditProgressionRowsAsync(NpgsqlConnection conn, string schema,
        TenantPartitioningMigrationPlan plan, CancellationToken token)
    {
        var progressionTable = $"{schema}.{EventProgressionTable.Name}";
        if (await regclassAsync(conn, progressionTable, token).ConfigureAwait(false) == null)
        {
            return;
        }

        await using var cmd = conn.CreateCommand($"select name, last_seq_id from {progressionTable} order by name");
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var name = reader.GetString(0);
            var lastSeqId = reader.GetInt64(1);
            plan.ProgressionRows.Add(classifyProgressionRow(name, lastSeqId));
        }
    }

    /// <summary>
    /// Classify an <c>mt_event_progression.name</c> against the shard / high-water identity grammar.
    /// High-water rows are checked first because <see cref="ShardName.TryParse"/> would otherwise
    /// (mis)read <c>HighWaterMark:{tenant}</c> as a store-global shard named "HighWaterMark".
    /// </summary>
    internal static ProgressionRowAudit classifyProgressionRow(string name, long lastSeqId)
    {
        if (name == HighWaterShardIdentity.StoreGlobal)
        {
            return new ProgressionRowAudit(name, lastSeqId, ProgressionRowKind.StoreGlobalHighWater, null);
        }

        if (name.StartsWith(HighWaterShardIdentity.PerTenantPrefix, StringComparison.Ordinal))
        {
            var tenantId = name.Substring(HighWaterShardIdentity.PerTenantPrefix.Length);
            return new ProgressionRowAudit(name, lastSeqId, ProgressionRowKind.PerTenantHighWater, tenantId);
        }

        if (ShardName.TryParse(name, out var shardName) && shardName != null)
        {
            return shardName.TenantId == null
                ? new ProgressionRowAudit(name, lastSeqId, ProgressionRowKind.StoreGlobalShard, null)
                : new ProgressionRowAudit(name, lastSeqId, ProgressionRowKind.PerTenantShard, shardName.TenantId);
        }

        return new ProgressionRowAudit(name, lastSeqId, ProgressionRowKind.Unrecognized, null);
    }

    private static async Task<string?> regclassAsync(NpgsqlConnection conn, string qualifiedName,
        CancellationToken token)
    {
        await using var cmd = conn.CreateCommand("select to_regclass(:name)::text").With("name", qualifiedName);
        var result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
        return result == null || result is DBNull ? null : (string)result;
    }

    private static async Task<long> countAsync(NpgsqlConnection conn, string sql, CancellationToken token)
    {
        await using var cmd = conn.CreateCommand(sql);
        var result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
        return result is long l ? l : Convert.ToInt64(result);
    }
}
