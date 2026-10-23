#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Schema;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal;

/// <summary>
/// Shared per-tenant cleanup for the paths that drop a tenant under
/// <c>UseTenantPartitionedEvents</c>: <c>AdvancedOperations.DeleteAllTenantDataAsync</c>
/// (via <see cref="TenantDataCleaner"/>), <c>AdvancedOperations.RemoveMartenManagedTenantsAsync</c>
/// and <c>ShardedTenancy.RemoveTenantAsync</c>.
/// <para>
/// Before the partition drop, the tenant's DCB tag rows and <c>[NaturalKey]</c> rows have to go
/// (<see cref="DeleteSideTableRowsAsync"/>, #5694, #5742): those tables are not partitioned by tenant, and
/// their foreign keys into <c>mt_events</c> / <c>mt_streams</c> refuse the drop while they remain. <c>RemoveMartenManagedTenantsAsync</c> is handed partition
/// suffixes, so it maps them to tenant ids first (<see cref="FindTenantIdsForSuffixesAsync"/>).
/// </para>
/// <para>
/// After the drop, they remove what it leaves behind (#4683):
///   * the freestanding <c>mt_events_sequence_{suffix}</c> sequence (no <c>OWNED BY</c> link
///     to the partition, so the partition drop leaves it orphaned)
///   * the per-tenant <c>mt_event_progression</c> rows -- one per projection per tenant
///     plus the per-tenant high-water row
/// Both leak slowly but unboundedly across drop-tenant cycles (trial-account churn, etc).
/// Both are found from tenant ids, not partition suffixes.
/// </para>
/// </summary>
internal static class PerTenantPartitionedCleanup
{
    /// <summary>
    /// Delete the rows of <paramref name="tenantIds"/> from the event store's side tables that are NOT
    /// partitioned by tenant but hold a foreign key into a table that is. They have to go first, before the
    /// tenants' events are deleted or their partitions dropped, because PostgreSQL refuses the drop while they
    /// remain. A cascading foreign key does not help: a cascade does not apply to a partition drop.
    /// <list type="bullet">
    ///   <item>the DCB <c>mt_event_tag_*</c> tables, keyed to <c>mt_events</c> (#5694, #5720)</item>
    ///   <item>the <c>[NaturalKey]</c> <c>mt_natural_key_*</c> tables, keyed to <c>mt_streams(id, tenant_id)</c>
    ///   (#5742)</item>
    /// </list>
    /// Only conjoined event stores share these tables between tenants. A table the schema has not created yet
    /// is skipped.
    /// </summary>
    public static async Task DeleteSideTableRowsAsync(
        StoreOptions options,
        IMartenDatabase database,
        IReadOnlyCollection<string> tenantIds,
        ILogger logger,
        CancellationToken token)
    {
        var events = options.EventGraph;
        if (tenantIds.Count == 0 || events.TenancyStyle != TenancyStyle.Conjoined)
        {
            return;
        }

        var candidates = new List<DbObjectName>();

        // In HStore mode tags live inline on mt_events.tags; no per-type tag tables exist
        if (events.DcbStorageMode != DcbStorageMode.HStore)
        {
            candidates.AddRange(events.TagTypes.Select(tagType =>
                new DbObjectName(events.DatabaseSchemaName, $"mt_event_tag_{tagType.TableSuffix}")));
        }

        candidates.AddRange(options.Projections.All.OfType<IAggregateProjection>()
            .Where(x => x.NaturalKeyDefinition != null)
            .Select(x => (DbObjectName)NaturalKeyTable.TableNameFor(events, x.NaturalKeyDefinition!)));

        if (candidates.Count == 0) return;

        var tables = await database.SchemaTables(token).ConfigureAwait(false);
        var builder = new BatchBuilder();
        var foundTables = false;

        foreach (var table in candidates.Distinct())
        {
            if (!tables.Contains(table)) continue;

            foundTables = true;
            logger.LogInformation("Trying to delete tenant information from table {Table} for tenants {TenantIds}",
                table.QualifiedName, string.Join(", ", tenantIds));
            builder.StartNewCommand();
            builder.Append($"delete from {table.QualifiedName} where tenant_id = ANY(");
            builder.AppendParameter(tenantIds.ToArray());
            builder.Append(")");
        }

        if (!foundTables) return;

        var batch = builder.Compile();
        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        try
        {
            batch.Connection = conn;
            await batch.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The tenant ids that <c>mt_tenant_partitions</c> maps to <paramref name="suffixes"/> in this database,
    /// read fresh, so they are exactly the rows <c>DropPartitionFromAllTables</c> is about to delete. A suffix
    /// is not always the tenant id: a Guid tenant's suffix is its hyphen-free "N" form (#4567), while its
    /// events and tag rows carry the canonical Guid string.
    /// </summary>
    public static async Task<IReadOnlyList<string>> FindTenantIdsForSuffixesAsync(
        StoreOptions options,
        IMartenDatabase database,
        string[] suffixes,
        CancellationToken token)
    {
        if (options.TenantPartitions == null || suffixes.Length == 0) return [];

        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        try
        {
            await using var cmd = conn.CreateCommand(
                $"select partition_value from {options.TenantPartitions.TenantsTableName.QualifiedName} where partition_suffix = ANY(:suffixes)");
            cmd.Parameters.AddWithValue("suffixes", suffixes);

            var tenantIds = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                tenantIds.Add(await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false));
            }

            return tenantIds;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // No tenant was ever registered in this database, so it has no tenant partition to drop
            return [];
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Capture the per-tenant sequence suffix for <paramref name="tenantIds"/> BEFORE the
    /// partition drop runs -- the Weasel managed-list registry may forget the tenant once
    /// <c>DropPartitionFromAllTablesForValue</c> succeeds, so we read the mapping first and
    /// hold it for the post-drop cleanup. Returned dictionary maps tenant id → suffix; missing
    /// keys mean "no suffix recorded in the registry for that tenant, skip the sequence drop"
    /// (defensive -- the partition drop is the source of truth for tenants whose partitions
    /// existed).
    /// </summary>
    public static IReadOnlyDictionary<string, string> CaptureSequenceSuffixes(
        StoreOptions options, Weasel.Core.Migrations.IDatabase database, IEnumerable<string> tenantIds)
    {
        var suffixes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!options.Events.UseTenantPartitionedEvents) return suffixes;

        // #4863/#4855: resolve the suffix from the registry view of the database the tenant
        // actually lives in — the store-wide snapshot may have been hydrated from a different
        // database entirely under multi-database tenancy.
        if (options.TenantPartitions?.Partitions.PartitionsFor(database) is not { } registry) return suffixes;

        foreach (var tenantId in tenantIds)
        {
            if (registry.TryGetValue(tenantId, out var suffix))
            {
                suffixes[tenantId] = suffix;
            }
        }
        return suffixes;
    }

    /// <summary>
    /// Run the post-partition-drop cleanup for the supplied tenant ids on
    /// <paramref name="database"/>. <paramref name="capturedSequenceSuffixes"/> should come
    /// from <see cref="CaptureSequenceSuffixes"/> called BEFORE the partition drop. Issues:
    /// <list type="bullet">
    ///   <item><c>DROP SEQUENCE IF EXISTS mt_events_sequence_{suffix}</c> for each captured suffix</item>
    ///   <item><c>DELETE FROM mt_event_progression WHERE name = ANY(...)</c> for the union of
    ///     row names matched by <see cref="MatchesAnyTenant"/></item>
    /// </list>
    /// Both statements run as a single batch on a fresh connection; the partition drop has
    /// already been committed by the caller. <c>IF EXISTS</c> + idempotent SELECT-then-DELETE
    /// keep re-runs safe.
    /// </summary>
    public static async Task RunAsync(
        StoreOptions options,
        IMartenDatabase database,
        IReadOnlyCollection<string> tenantIds,
        IReadOnlyDictionary<string, string> capturedSequenceSuffixes,
        CancellationToken token)
    {
        if (!options.Events.UseTenantPartitionedEvents) return;
        if (tenantIds.Count == 0) return;

        var eventsSchema = options.Events.DatabaseSchemaName;

        await DeleteProjectionFloorsAsync(eventsSchema, database, tenantIds, token).ConfigureAwait(false);

        var progressionRows = await CollectMatchingProgressionRowsAsync(
            eventsSchema, database, tenantIds, token).ConfigureAwait(false);

        if (capturedSequenceSuffixes.Count == 0 && progressionRows.Count == 0) return;

        var builder = new BatchBuilder();
        foreach (var suffix in capturedSequenceSuffixes.Values)
        {
            builder.StartNewCommand();
            // #4924 — one place builds this name, so the quoting cannot drift between the create and drop
            // paths. The suffix is the raw tenant id and may contain '-'.
            builder.Append(
                $"DROP SEQUENCE IF EXISTS {Events.Schema.PerTenantEventSequences.QuotedSequenceName(eventsSchema, suffix)}");
        }

        if (progressionRows.Count > 0)
        {
            builder.StartNewCommand();
            builder.Append($"DELETE FROM \"{eventsSchema}\".\"mt_event_progression\" WHERE name = ANY(");
            builder.AppendParameter(progressionRows.ToArray());
            builder.Append(")");
        }

        var batch = builder.Compile();
        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        try
        {
            batch.Connection = conn;
            await batch.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// jasperfx#809 (#5740): a removed tenant's projection start floors go with it. A re-added tenant starts a
    /// fresh per-tenant sequence at 1, so a floor left behind by its previous incarnation would make its
    /// projections skip every event below that floor -- events that, this time, are real. Unlike the
    /// progression rows these are not leftovers of the partition drop, so they are deleted whether or not
    /// anything else was found. A floor table the schema has not been migrated to yet has nothing to delete.
    /// </summary>
    private static async Task DeleteProjectionFloorsAsync(string eventsSchema, IMartenDatabase database,
        IReadOnlyCollection<string> tenantIds, CancellationToken token)
    {
        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        try
        {
            var command = conn.CreateCommand(
                $"DELETE FROM \"{eventsSchema}\".\"{Events.Schema.ProjectionFloorTable.Name}\" WHERE tenant_id = ANY(:tenants)");
            command.Parameters.AddWithValue("tenants", tenantIds.ToArray());
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Nothing was ever pinned in a database without the table.
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Collect <c>mt_event_progression.name</c> rows that belong to any of
    /// <paramref name="tenantIds"/>. We resolve membership via two paths so we never use a
    /// LIKE pattern that could false-positive-match a projection name happening to end with a
    /// tenant id:
    /// <list type="bullet">
    ///   <item>Per-tenant <c>HighWaterMark:{tenantId}</c> rows: matched exactly via
    ///     <see cref="HighWaterShardIdentity.PerTenantPrefix"/> -- ShardName intentionally
    ///     collapses HighWaterMark identities to the constant, so they don't round-trip
    ///     through <see cref="ShardName.TryParse"/>.</item>
    ///   <item>Per-projection per-tenant rows: <see cref="ShardName.TryParse"/> recognises
    ///     <c>Name:ShardKey:Tenant</c> / <c>Name:V{n}:ShardKey:Tenant</c>; tenant slot match
    ///     against the input set.</item>
    /// </list>
    /// Store-global rows ("HighWaterMark", "MyProjection:All", etc) never match -- they have
    /// no tenant slot or use the literal HighWaterMark constant.
    /// </summary>
    private static async Task<List<string>> CollectMatchingProgressionRowsAsync(
        string eventsSchema,
        IMartenDatabase database,
        IReadOnlyCollection<string> tenantIds,
        CancellationToken token)
    {
        var matched = new List<string>();
        var tenantSet = new HashSet<string>(tenantIds, StringComparer.Ordinal);

        await using var conn = database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        try
        {
            await using var cmd = conn.CreateCommand(
                $"SELECT name FROM \"{eventsSchema}\".\"mt_event_progression\"");
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var name = await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false);
                if (MatchesAnyTenant(name, tenantSet))
                {
                    matched.Add(name);
                }
            }
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
        return matched;
    }

    internal static bool MatchesAnyTenant(string progressionName, ICollection<string> tenantIds)
    {
        if (progressionName.StartsWith(HighWaterShardIdentity.PerTenantPrefix, StringComparison.Ordinal))
        {
            var nameTenantId = progressionName.Substring(HighWaterShardIdentity.PerTenantPrefix.Length);
            return tenantIds.Contains(nameTenantId);
        }

        return ShardName.TryParse(progressionName, out var shard)
            && shard?.TenantId is { } shardTenant
            && tenantIds.Contains(shardTenant);
    }
}
