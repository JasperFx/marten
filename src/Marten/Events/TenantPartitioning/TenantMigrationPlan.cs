#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Marten.Events.TenantPartitioning;

/// <summary>
/// One tenant's slice of the Phase 1 inventory for a conjoined → tenant-partitioned migration.
/// </summary>
/// <param name="TenantId">The tenant id as stored in the source's <c>tenant_id</c> columns</param>
/// <param name="EventCount">Number of events (archived included) the source holds for this tenant</param>
/// <param name="StreamCount">Number of <c>mt_streams</c> rows the source holds for this tenant</param>
/// <param name="MaxSequence">The tenant's highest source <c>seq_id</c> (0 when the tenant has no events)</param>
/// <param name="AlreadyCompleted">
/// True when the target's <c>mt_tenant_migration_log</c> already records this tenant as completed,
/// so a resumed run will skip it
/// </param>
public record TenantMigrationPlanItem(
    string TenantId,
    long EventCount,
    long StreamCount,
    long MaxSequence,
    bool AlreadyCompleted);

/// <summary>
/// The Phase 1 inventory + plan for a conjoined → tenant-partitioned migration
/// (<see cref="ConjoinedToPartitionedMigration.BuildPlanAsync"/>). Building the plan reads the
/// source (and the target's migration log) but never moves data — it is the dry-run.
/// </summary>
public class TenantMigrationPlan
{
    public TenantMigrationPlan(IReadOnlyList<TenantMigrationPlanItem> tenants)
    {
        Tenants = tenants;
    }

    /// <summary>Per-tenant inventory in the order the migration will process them.</summary>
    public IReadOnlyList<TenantMigrationPlanItem> Tenants { get; }

    /// <summary>Total number of events across every tenant in the plan.</summary>
    public long TotalEvents => Tenants.Sum(x => x.EventCount);

    /// <summary>The tenants a run of the migration would actually move (not yet completed).</summary>
    public IReadOnlyList<TenantMigrationPlanItem> PendingTenants =>
        Tenants.Where(x => !x.AlreadyCompleted).ToList();
}
