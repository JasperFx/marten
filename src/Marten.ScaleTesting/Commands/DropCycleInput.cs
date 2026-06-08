using JasperFx;
using JasperFx.CommandLine;
using Marten.ScaleTesting.Seeding;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// Inputs for the <c>drop-cycle</c> subcommand (#4683 Fix 3). Picks one tenant from an
/// already-seeded store, drops it via <c>DeleteAllTenantDataAsync</c>, verifies every
/// per-tenant artifact is gone and the other tenants are untouched, and (unless
/// <c>--skip-readd</c>) re-adds the tenant and verifies the fresh state.
/// </summary>
public sealed class DropCycleInput: NetCoreInput
{
    [Description("Total tenants the store has been seeded with (used to compute the tenant id naming convention + sample other tenants). Default: 50.")]
    public int TenantsFlag { get; set; } = 50;

    [Description("Zero-based index of the tenant to drop. Default: 0 (so tenant_0000 by the harness's naming convention).")]
    public int TenantIndexFlag { get; set; } = 0;

    [Description("Skip the re-add + re-seed verification phase. Default: false. Use --skip-readd to just exercise the drop cleanup.")]
    public bool SkipReaddFlag { get; set; }

    [Description("After re-adding the tenant, append this many fresh events to verify the per-tenant sequence + progression machinery starts clean. Default: 50.")]
    public int ReaddEventCountFlag { get; set; } = 50;

    public string TenantId() => new SeedOptions(TenantCount: TenantsFlag).TenantId(TenantIndexFlag);
}
