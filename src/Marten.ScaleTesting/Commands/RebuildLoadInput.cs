using JasperFx;
using JasperFx.CommandLine;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// Inputs for the <c>rebuildload</c> subcommand — marten#4884 (epic jasperfx#486 WS6/WS3): load-test
/// many tenant shards rebuilding concurrently and sweep the three governor knobs so the shipped
/// defaults can be confirmed (or a change recommended) from evidence:
/// <list type="bullet">
///   <item><c>MaxConcurrentRebuildsPerDatabase</c> — the outer per-database rebuild fan-out cap
///     (jasperfx#420/#463; Marten's default is <c>max(1, MaxPoolSize / 8)</c>)</item>
///   <item><c>MaxConcurrentEventLoadsPerDatabase</c> (default 4) — inner event-load semaphore</item>
///   <item><c>MaxConcurrentBatchWritesPerDatabase</c> (default 4) — inner batch-write semaphore</item>
/// </list>
/// The harness sets the governors before building the store, runs a full rebuild of every
/// registered projection under the cap, and samples per-database connections + progression
/// lock-waits + wall-clock for each configuration in the sweep. Tuning evidence only — the
/// harness never changes a shipped default.
/// </summary>
public sealed class RebuildLoadInput: NetCoreInput
{
    [Description("Number of tenants (per-tenant partitioned) to rebuild across. Default: 100.")]
    public int TenantsFlag { get; set; } = 100;

    [Description("Number of independent async projections to register + rebuild (the outer rebuild cap governs how many rebuild concurrently). Default: 4.")]
    public int ProjectionsFlag { get; set; } = 4;

    [Description("Events seeded per tenant. Default: 2000.")]
    public int EventsPerTenantFlag { get; set; } = 2000;

    [Description("marten#4882 lineage: rebuild over N pooled shard databases instead of one. Default: 1.")]
    public int DatabasesFlag { get; set; } = 1;

    [Description("Comma-separated outer rebuild-cap values to sweep (MaxConcurrentRebuildsPerDatabase). Default: 2,4,8.")]
    public string CapsFlag { get; set; } = "2,4,8";

    [Description("Comma-separated inner event-load governor values to sweep (MaxConcurrentEventLoadsPerDatabase). Default: 4.")]
    public string LoadGovernorsFlag { get; set; } = "4";

    [Description("Comma-separated inner batch-write governor values to sweep (MaxConcurrentBatchWritesPerDatabase). Default: 4.")]
    public string WriteGovernorsFlag { get; set; } = "4";

    [Description("Also run each configuration with EnableExtendedProgressionTracking on (heavier per-cell write profile) to measure its cost. Default: false.")]
    public bool SweepExtendedProgressionFlag { get; set; }

    [Description("Per-shard rebuild timeout, in seconds. Default: 600.")]
    public int ShardTimeoutSecondsFlag { get; set; } = 600;

    [Description("pg_stat_activity sample interval in seconds. Default: 0.5.")]
    public double SampleSecondsFlag { get; set; } = 0.5;

    [Description("Drop + recreate the dedicated schema (and shard databases) before seeding. Default: false.")]
    public bool WipeFlag { get; set; }

    [Description("Skip seeding (assume the data is already present from a prior run). Default: false.")]
    public bool SkipSeedFlag { get; set; }

    [Description("Optional path for a JSON metrics file (one entry per swept configuration + the recommendation).")]
    public string? MetricsFlag { get; set; }
}
