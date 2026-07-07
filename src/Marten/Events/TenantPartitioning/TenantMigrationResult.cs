#nullable enable
using System.Collections.Generic;

namespace Marten.Events.TenantPartitioning;

/// <summary>
/// Outcome of one <see cref="ConjoinedToPartitionedMigration.ExecuteAsync"/> run.
/// </summary>
public class TenantMigrationResult
{
    /// <summary>Tenants moved by THIS run, in completion order.</summary>
    public List<string> MigratedTenants { get; } = new();

    /// <summary>Tenants skipped by THIS run because the migration log already recorded them as completed.</summary>
    public List<string> SkippedTenants { get; } = new();

    /// <summary>Total events copied by this run.</summary>
    public long EventsCopied { get; set; }
}
