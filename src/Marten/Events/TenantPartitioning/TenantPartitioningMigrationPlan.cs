#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Marten.Events.TenantPartitioning;

/// <summary>
/// #4682 — classification of an existing <c>mt_event_progression</c> row, produced by the
/// migration's audit pass. The conjoined → tenant-partitioned migration must be able to account
/// for every progression row before the table swap: known store-global and per-tenant identities
/// are carried across untouched, while anything that doesn't match the shard grammar is a
/// hand-rolled straggler that needs operator attention.
/// </summary>
public enum ProgressionRowKind
{
    /// <summary>The single store-global high-water row, <c>HighWaterMark</c>.</summary>
    StoreGlobalHighWater,

    /// <summary>A per-tenant high-water row, <c>HighWaterMark:{tenantId}</c>.</summary>
    PerTenantHighWater,

    /// <summary>A store-global projection shard, e.g. <c>Invoice:All</c> or <c>Invoice:V7:All</c>.</summary>
    StoreGlobalShard,

    /// <summary>A per-tenant projection shard, e.g. <c>Invoice:All:acme</c> or <c>Invoice:V7:All:acme</c>.</summary>
    PerTenantShard,

    /// <summary>Does not match any known identity grammar — a hand-rolled row needing review.</summary>
    Unrecognized
}

/// <summary>
/// #4682 — one audited <c>mt_event_progression</c> row.
/// </summary>
public sealed record ProgressionRowAudit(string Name, long LastSeqId, ProgressionRowKind Kind, string? TenantId);

/// <summary>
/// #4682 — per-tenant event inventory taken during the migration's Prepare phase. The seed value
/// for each tenant's new <c>mt_events_sequence_{suffix}</c> is <see cref="MaxSeqId"/> + 1.
/// </summary>
public sealed record TenantEventInventory(
    string TenantId,
    long EventCount,
    long MaxSeqId,
    bool PartitionRegistered,
    string? PartitionSuffix);

/// <summary>
/// #4682 — the output of the conjoined → <c>UseTenantPartitionedEvents</c> migration's Prepare
/// phase (the <c>--dry-run</c> result). It is read-only: building a plan touches no data, only
/// inventories the source store and validates the prerequisites so an operator can review the
/// scope before any data movement begins. Phases 2 (per-tenant copy + attach) and 3 (swap +
/// cleanup) consume this plan.
/// </summary>
public sealed class TenantPartitioningMigrationPlan
{
    /// <summary>
    /// Hard prerequisite failures. When non-empty the migration must not proceed; these are
    /// configuration / data problems an operator has to fix first.
    /// </summary>
    public List<string> Errors { get; } = new();

    /// <summary>
    /// Non-blocking findings (e.g. tenants with events but no registered partition yet, or
    /// hand-rolled progression rows) that an operator should review but that do not by themselves
    /// stop the migration.
    /// </summary>
    public List<string> Warnings { get; } = new();

    /// <summary>Per-tenant event inventory, ordered by tenant id.</summary>
    public List<TenantEventInventory> Tenants { get; } = new();

    /// <summary>Audit of every <c>mt_event_progression</c> row in the source store.</summary>
    public List<ProgressionRowAudit> ProgressionRows { get; } = new();

    /// <summary>
    /// Count of <c>mt_events</c> rows with a null <c>tenant_id</c>. Must be zero to migrate: a row
    /// with no tenant cannot be routed to a tenant partition.
    /// </summary>
    public long EventsWithoutTenant { get; set; }

    /// <summary>True when there are no hard prerequisite failures.</summary>
    public bool CanProceed => Errors.Count == 0;

    /// <summary>Total event rows across all tenants.</summary>
    public long TotalEvents => Tenants.Sum(x => x.EventCount);

    /// <summary>Tenants that have events but no registered partition in <c>mt_tenant_partitions</c>.</summary>
    public IEnumerable<TenantEventInventory> TenantsMissingPartitions =>
        Tenants.Where(x => !x.PartitionRegistered);

    /// <summary>Progression rows the audit could not classify (hand-rolled stragglers).</summary>
    public IEnumerable<ProgressionRowAudit> UnrecognizedProgressionRows =>
        ProgressionRows.Where(x => x.Kind == ProgressionRowKind.Unrecognized);
}
