#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.HighWater;
using Marten.Storage;
using Weasel.Postgresql;

namespace Marten.Internal;

/// <summary>
/// Shared per-tenant cleanup for the two paths that drop a tenant under
/// <c>UseTenantPartitionedEvents</c>: <c>AdvancedOperations.DeleteAllTenantDataAsync</c>
/// (via <see cref="TenantDataCleaner"/>) and <c>AdvancedOperations.RemoveMartenManagedTenantsAsync</c>.
/// Both routes drop the tenant's partition tables but neither, until #4683, dropped:
///   * the freestanding <c>mt_events_sequence_{suffix}</c> sequence (no <c>OWNED BY</c> link
///     to the partition, so the partition drop leaves it orphaned)
///   * the per-tenant <c>mt_event_progression</c> rows -- one per projection per tenant
///     plus the per-tenant high-water row
/// Both leak slowly but unboundedly across drop-tenant cycles (trial-account churn, etc).
/// </summary>
internal static class PerTenantPartitionedCleanup
{
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
