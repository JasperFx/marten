#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events.TenantPartitioning;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace TenantPartitionedEventsTests.Migration;

public record MigrationStreamStarted(string Name);

public record MigrationThingHappened(int Number);

/// <summary>
/// #4682 Phase 1 — the read-only Prepare / dry-run path of the conjoined →
/// UseTenantPartitionedEvents migration. Verifies prerequisite validation, per-tenant inventory
/// (counts + high-water seq ids), and the mt_event_progression audit classifier.
/// </summary>
public class conjoined_to_partitioned_migration_plan: IAsyncLifetime
{
    private readonly string _schema = "tp_migrate_p" + Environment.ProcessId;

    public async Task InitializeAsync()
    {
        // Start from a clean schema so the conjoined seed and the dry-run read are deterministic.
        await using var store = sourceStore(AutoCreate.All);
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DocumentStore sourceStore(AutoCreate autoCreate) =>
        DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.AutoCreateSchemaObjects = autoCreate;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

    // The store the operator would configure for the *target* state: same database / schema,
    // conjoined + partitioning turned on, but AutoCreate.None so building it never disturbs the
    // existing conjoined schema. The dry-run only reads.
    private DocumentStore targetStore() =>
        DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseTenantPartitionedEvents = true;
        });

    private async Task seedConjoinedEventsAsync()
    {
        await using var store = sourceStore(AutoCreate.All);

        // Single-threaded, sequential appends so the global sequence is deterministic:
        // acme => seq 1..5 (max 5), globex => 6..8 (max 8), initech => 9..10 (max 10).
        await appendAsync(store, "acme", 5);
        await appendAsync(store, "globex", 3);
        await appendAsync(store, "initech", 2);
    }

    private static async Task appendAsync(IDocumentStore store, string tenantId, int count)
    {
        await using var session = store.LightweightSession(tenantId);
        var streamId = $"{tenantId}-stream";
        session.Events.StartStream(streamId, new MigrationStreamStarted(tenantId));
        for (var i = 1; i < count; i++)
        {
            session.Events.Append(streamId, new MigrationThingHappened(i));
        }

        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task dry_run_inventories_tenants_and_validates_prerequisites()
    {
        await seedConjoinedEventsAsync();

        await using var target = targetStore();
        var database = (MartenDatabase)target.Tenancy.Default.Database;

        var plan = await database.CreateTenantPartitioningMigrationPlanAsync();

        plan.CanProceed.ShouldBeTrue();
        plan.Errors.ShouldBeEmpty();
        plan.EventsWithoutTenant.ShouldBe(0);
        plan.TotalEvents.ShouldBe(10);

        plan.Tenants.Select(x => x.TenantId).ShouldBe(new[] { "acme", "globex", "initech" });

        var acme = plan.Tenants.Single(x => x.TenantId == "acme");
        acme.EventCount.ShouldBe(5);
        acme.MaxSeqId.ShouldBe(5); // per-tenant sequence will START WITH max+1 = 6

        plan.Tenants.Single(x => x.TenantId == "globex").MaxSeqId.ShouldBe(8);
        plan.Tenants.Single(x => x.TenantId == "initech").MaxSeqId.ShouldBe(10);

        // No partitions registered yet => every tenant flagged, but it's only a warning.
        plan.TenantsMissingPartitions.Select(x => x.TenantId)
            .ShouldBe(new[] { "acme", "globex", "initech" });
        plan.Warnings.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task validation_blocks_when_partitioning_is_not_configured()
    {
        await seedConjoinedEventsAsync();

        // Build the plan against a store that is still plain conjoined (UseTenantPartitionedEvents off).
        await using var store = sourceStore(AutoCreate.None);
        var database = (MartenDatabase)store.Tenancy.Default.Database;

        var plan = await database.CreateTenantPartitioningMigrationPlanAsync();

        plan.CanProceed.ShouldBeFalse();
        plan.Errors.ShouldContain(e => e.Contains("UseTenantPartitionedEvents"));
    }

    [Theory]
    [InlineData("HighWaterMark", ProgressionRowKind.StoreGlobalHighWater, null)]
    [InlineData("HighWaterMark:acme", ProgressionRowKind.PerTenantHighWater, "acme")]
    [InlineData("Invoice:All", ProgressionRowKind.StoreGlobalShard, null)]
    [InlineData("Invoice:V7:All", ProgressionRowKind.StoreGlobalShard, null)]
    [InlineData("Invoice:All:acme", ProgressionRowKind.PerTenantShard, "acme")]
    [InlineData("Invoice:V7:All:acme", ProgressionRowKind.PerTenantShard, "acme")]
    [InlineData("totally-hand-rolled", ProgressionRowKind.Unrecognized, null)]
    public void classifies_progression_rows(string name, ProgressionRowKind expectedKind, string? expectedTenant)
    {
        var audit = MartenDatabase.classifyProgressionRow(name, 42);

        audit.Kind.ShouldBe(expectedKind);
        audit.TenantId.ShouldBe(expectedTenant);
        audit.LastSeqId.ShouldBe(42);
    }
}
