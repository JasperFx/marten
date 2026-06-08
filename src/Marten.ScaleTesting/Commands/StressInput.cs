using JasperFx;
using JasperFx.CommandLine;
using Marten.ScaleTesting.Seeding;

namespace Marten.ScaleTesting.Commands;

/// <summary>
/// Inputs for the chained <c>stress</c> subcommand. Inherits the seed knobs
/// (tenants/events/buckets/writers/seed) and adds rebuild + validate knobs so
/// one CLI invocation runs seed → rebuild → validate.
/// </summary>
public sealed class StressInput: NetCoreInput
{
    // ---- seed flags ------------------------------------------------------

    [Description("Number of tenants to seed under conjoined multi-tenancy. Default: 50.")]
    public int TenantsFlag { get; set; } = 50;

    [Description("Events per tenant. Default: 400,000 (×50 tenants ≈ 20M events).")]
    public int EventsPerTenantFlag { get; set; } = 400_000;

    [Description("Number of hash partition buckets. Default: 8.")]
    public int BucketsFlag { get; set; } = 8;

    [Description("Parallel writer task count for the seeder. Default: 8.")]
    public int WritersFlag { get; set; } = 8;

    [Description("Root seed for deterministic stream generation. Default: 42.")]
    public int SeedFlag { get; set; } = 42;

    [Description("Wipe the event store schema before seeding. Default: false (idempotent rerun).")]
    public bool WipeFlag { get; set; }

    // ---- rebuild flags ---------------------------------------------------

    [Description("Projection name to rebuild after seeding. Default: TelehealthComposite.")]
    public string ProjectionFlag { get; set; } = "TelehealthComposite";

    [Description("Per-shard rebuild timeout, in seconds. Default: 1800 (30 min) — larger than the rebuild subcommand's default to accommodate full-20M runs.")]
    public int ShardTimeoutSecondsFlag { get; set; } = 1800;

    // ---- validate flags --------------------------------------------------

    [Description("Baseline JSON file path for the validate phase. If missing, current capture is written as the baseline.")]
    public string BaselineFlag { get; set; } = "scaletest-baseline.json";

    [Description("Skip the validate phase entirely (just seed + rebuild). Useful for the very first stress run before a baseline exists.")]
    public bool SkipValidateFlag { get; set; }

    // ---- instrumentation flags (#4684 Phase E.1) ----------------------------

    [Description("Enable per-period throughput sampling against mt_event_progression + Npgsql command counting during the rebuild phase. Off by default.")]
    public bool InstrumentFlag { get; set; }

    [Description("Optional CSV trace path. Implies --instrument. One row per progress sample: timestamp, sequence_id, delta, events_per_second, npgsql_commands_in_interval.")]
    public string? InstrumentTraceFlag { get; set; }

    [Description("Optional CSV trace path for the progression lock-wait sampler (#4684 Phase E.2). One row per sample: timestamp, current waiter count, max single-waiter wait in ms. Implies --instrument.")]
    public string? InstrumentLockTraceFlag { get; set; }

    [Description("Sample interval for the progression poller when --instrument is on. Default 1s.")]
    public double InstrumentSampleSecondsFlag { get; set; } = 1.0;

    [Description("Metrics JSON output path. When set (and --instrument is on), the instrumentation snapshot is written here as JSON.")]
    public string? MetricsFlag { get; set; }

    public SeedOptions ToSeedOptions() => new(
        TenantCount: TenantsFlag,
        EventsPerTenant: EventsPerTenantFlag,
        HashBuckets: BucketsFlag,
        WriterTasks: WritersFlag,
        Seed: SeedFlag);
}
