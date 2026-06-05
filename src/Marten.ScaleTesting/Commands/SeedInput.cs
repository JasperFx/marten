using JasperFx;
using JasperFx.CommandLine;
using Marten.ScaleTesting.Seeding;

namespace Marten.ScaleTesting.Commands;

public sealed class SeedInput: NetCoreInput
{
    [Description("Number of tenants to seed under conjoined multi-tenancy. Default: 50.")]
    public int TenantsFlag { get; set; } = 50;

    [Description("Events per tenant. Default: 400,000 (×50 tenants ≈ 20M events).")]
    public int EventsPerTenantFlag { get; set; } = 400_000;

    [Description("Number of hash partition buckets for the conjoined tenancy. Default: 8.")]
    public int BucketsFlag { get; set; } = 8;

    [Description("Parallel writer task count. Default: 8.")]
    public int WritersFlag { get; set; } = 8;

    [Description("Root seed for deterministic stream generation. Default: 42.")]
    public int SeedFlag { get; set; } = 42;

    [Description("Wipe the event store schema before seeding. Default: false (idempotent rerun).")]
    public bool WipeFlag { get; set; }

    public SeedOptions ToSeedOptions() => new(
        TenantCount: TenantsFlag,
        EventsPerTenant: EventsPerTenantFlag,
        HashBuckets: BucketsFlag,
        WriterTasks: WritersFlag,
        Seed: SeedFlag);
}
