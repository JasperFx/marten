namespace Marten.ScaleTesting.Seeding;

/// <summary>
/// Tunable inputs for the event seeder. Defaults match the #4666 issue
/// targets (50 tenants × 400K events/tenant = 20M events under 8 hash
/// buckets). Override from the CLI for smaller dev-loop runs.
/// </summary>
public sealed record SeedOptions(
    int TenantCount = 50,
    int EventsPerTenant = 400_000,
    int HashBuckets = 8,
    int WriterTasks = 8,
    int Seed = 42,
    string TenantPrefix = "tenant_",
    int BatchBufferCapacity = 1024)
{
    public int TotalEvents => TenantCount * EventsPerTenant;

    /// <summary>
    /// Tenant id at a given index. Stable for a given <see cref="TenantPrefix"/>
    /// so reruns are idempotent.
    /// </summary>
    public string TenantId(int index) => $"{TenantPrefix}{index:D4}";
}
