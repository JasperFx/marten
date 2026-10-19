#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using Marten.Schema;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Storage;

/// <summary>
/// #4596 Phase 2 — implements jasperfx#407's <see cref="ICrossTenantRebuildSource"/>
/// for Marten. Returns the distinct tenants that carry data for the named
/// projection, used by <c>CrossTenantRebuild.RebuildEverywhereAsync</c> to fan
/// out N independent per-tenant rebuilds.
/// </summary>
public partial class MartenDatabase : ICrossTenantRebuildSource
{
    /// <summary>
    /// The tenants to rebuild when rebuilding <paramref name="projectionName"/>
    /// across all tenants. Source of truth: the registered partitions in
    /// <see cref="MartenManagedTenantListPartitions"/>'s
    /// <c>mt_tenant_partitions</c> table — every tenant that has a partition
    /// is a candidate (it has either committed events or is on track to).
    /// We return all registered partitions rather than scanning
    /// <c>mt_event_progression</c> for prior <c>(name, tenant_id)</c> rows
    /// because a freshly-registered tenant that has never been rebuilt yet
    /// would otherwise be skipped — that's the opposite of what
    /// "rebuild X everywhere" should do.
    /// <para>
    /// The lookup table is created by a full schema apply or by registering the
    /// first tenant, not by the event storage, so a database whose schema is
    /// created lazily has none until its first tenant is registered. No tenant
    /// lives there yet: the answer is an empty list, on which the daemon keeps
    /// the store-global shard (<c>StartAllAsync</c>, the coordinator's per-tenant
    /// shard expansion, the store-global rebuild and <c>CatchUpAsync</c>) exactly
    /// as it does for an empty table.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<string>> FindRebuildTenantsAsync(string projectionName, CancellationToken token)
    {
        try
        {
            return await FetchManagedTenantIdsAsync(token).ConfigureAwait(false);
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // Answered here rather than in FetchManagedTenantIdsAsync: its other callers handle a
            // missing table themselves, and ShardedTenancy's tenant removal reads it as "nothing
            // was ever provisioned on this shard" and skips its cleanup.
            return [];
        }
    }

    /// <summary>
    /// #4862 — the Marten-managed tenant list for this database, read fresh from the
    /// <c>mt_tenant_partitions</c> lookup table. This is the single source of truth for
    /// "which tenants live here" under Marten-managed partitioning; it backs both the
    /// cross-tenant rebuild fan-out above and
    /// <see cref="TenantPartitionedSingleDatabaseTenancy"/>'s usage descriptor
    /// <c>TenantIds</c> (so hosts that distribute agents per tenant know which
    /// tenants to start agents for).
    /// Returns an empty list when Marten-managed partitioning isn't active.
    /// </summary>
    public async Task<IReadOnlyList<string>> FetchManagedTenantIdsAsync(CancellationToken token)
    {
        var tenantPartitions = Options.TenantPartitions;
        if (tenantPartitions == null)
        {
            return [];
        }

        await using var conn = CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        try
        {
            // The MartenManagedTenantListPartitions wrapper keeps an in-memory
            // dictionary of partitions, but it isn't reloaded automatically
            // after another node calls AddMartenManagedTenantsAsync. Read the
            // authoritative list directly from the lookup table so this call
            // sees every registered tenant regardless of which node added them.
            var tenantsTableName = tenantPartitions.TenantsTableName;
            await using var cmd = conn.CreateCommand(
                $"select partition_value from {tenantsTableName} order by partition_value");
            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);

            var tenants = new List<string>();
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                tenants.Add(reader.GetString(0));
            }

            return tenants;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }
}
