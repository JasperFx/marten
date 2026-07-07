#nullable enable
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Descriptors;
using Npgsql;

namespace Marten.Storage;

/// <summary>
/// #4862 — the tenancy for the "single database + <c>Events.UseTenantPartitionedEvents</c>"
/// permutation. Behaviorally identical to <see cref="DefaultTenancy"/> (one conjoined
/// database, every tenant lives in it) except that <see cref="DescribeDatabasesAsync"/>
/// surfaces the Marten-managed tenant list on the main database descriptor's
/// <c>TenantIds</c>. Hosts that distribute agents per identity (Wolverine-managed event
/// subscription distribution) enumerate the descriptor to fan agents out per tenant once
/// <c>IEventStore.DistributesAgentsPerTenant</c> is on — DefaultTenancy leaves
/// <c>TenantIds</c> empty (it has no per-tenant registration to copy from, unlike the
/// Master-Table/SingleServer/Static/Sharded tenancies), so the broadened gate would fan
/// out to nothing.
/// </summary>
/// <remarks>
/// Deliberately a subclass of <see cref="DefaultTenancy"/> rather than a freestanding
/// <see cref="ITenancy"/>: several behavior gates dispatch on the concrete type
/// (<c>DocumentStore</c>'s lazy <c>Initialize()</c>, the
/// <c>AdvancedOperations.AddMartenManagedTenantsAsync</c> /
/// <c>RemoveMartenManagedTenantsAsync</c> routing, <c>TenantDataCleaner</c>,
/// <c>ProjectionCoordinator</c>, <c>WaitForNonStaleProjectionDataAsync</c>) and this
/// permutation must keep behaving as a single-database DefaultTenancy store for all of
/// them. Re-listing <see cref="ITenancy"/> in the base list re-maps the interface's
/// <c>DescribeDatabasesAsync</c> slot onto the override below while every other member
/// stays the inherited DefaultTenancy implementation.
/// </remarks>
internal class TenantPartitionedSingleDatabaseTenancy: DefaultTenancy, ITenancy
{
    public TenantPartitionedSingleDatabaseTenancy(NpgsqlDataSource dataSource, StoreOptions options)
        : base(dataSource, options)
    {
    }

    public new async ValueTask<DatabaseUsage> DescribeDatabasesAsync(CancellationToken token)
    {
        var usage = await base.DescribeDatabasesAsync(token).ConfigureAwait(false);

        // Read the authoritative tenant list fresh from the mt_tenant_partitions lookup
        // table — the same source ICrossTenantRebuildSource.FindRebuildTenantsAsync uses —
        // so tenants registered after the store was built (and by other nodes) are visible
        // without any cache invalidation.
        if (Options.TenantPartitions != null && Default.Database is MartenDatabase martenDatabase)
        {
            try
            {
                var tenantIds = await martenDatabase.FetchManagedTenantIdsAsync(token).ConfigureAwait(false);
                foreach (var tenantId in tenantIds)
                {
                    usage.MainDatabase!.TenantIds.Fill(tenantId);
                }
            }
            catch (NpgsqlException)
            {
                // Storage may not exist yet (mt_tenant_partitions not provisioned) —
                // leave TenantIds empty rather than failing the whole description.
            }
        }

        return usage;
    }
}
