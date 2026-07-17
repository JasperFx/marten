using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon.Progress;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Internals;

// #4962 / jasperfx#435 — targeted per-cell progression read. mt_event_progression.name holds a full
// ShardName.Identity, so (projectionName, tenantId) can map to several rows; the resolution rules are:
// candidates match parsed Name + TenantId (null tenant = bare store-global only), newest version wins.
public class read_projection_progress : OneOffConfigurationsContext, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
        await theStore.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    private async Task seedProgression(params (ShardName name, long sequence)[] rows)
    {
        foreach (var (name, sequence) in rows)
        {
            theSession.QueueOperation(new InsertProjectionProgress(theStore.Events, new EventRange(name, sequence)));
        }

        await theSession.SaveChangesAsync();
    }

    private ValueTask<ProjectionProgressRow?> read(string projectionName, string? tenantId) =>
        ((IEventDatabase)theStore.Tenancy.Default.Database)
        .ReadProjectionProgressAsync(projectionName, tenantId, CancellationToken.None);

    [Fact]
    public async Task reads_the_store_global_row()
    {
        await seedProgression((ShardName.Compose("Orders"), 42));

        var row = await read("Orders", null);

        row.ShouldNotBeNull();
        row.ProjectionName.ShouldBe("Orders");
        row.TenantId.ShouldBeNull();
        row.Sequence.ShouldBe(42);
        // Marten models the columns but writes neither — always null (jasperfx#519).
        row.AgentStatus.ShouldBeNull();
        row.LastHeartbeat.ShouldBeNull();
    }

    [Fact]
    public async Task returns_null_when_no_row_exists()
    {
        await seedProgression((ShardName.Compose("Orders"), 42));

        (await read("Unobserved", null)).ShouldBeNull();
    }

    [Fact]
    public async Task newest_version_wins_across_a_blue_green_deploy()
    {
        await seedProgression(
            (ShardName.Compose("Orders"), 10),                       // Orders:All        (V1)
            (ShardName.Compose("Orders", version: 2), 25),           // Orders:V2:All
            (ShardName.Compose("Orders", version: 3), 40));          // Orders:V3:All

        var row = await read("Orders", null);

        row.ShouldNotBeNull();
        row.Sequence.ShouldBe(40);
    }

    [Fact]
    public async Task null_tenant_matches_only_the_bare_store_global_row()
    {
        await seedProgression(
            (ShardName.Compose("Orders"), 10),                       // Orders:All
            (ShardName.Compose("Orders", tenantId: "tenant1"), 99)); // Orders:All:tenant1

        (await read("Orders", null))!.Sequence.ShouldBe(10);
        (await read("Orders", "tenant1"))!.Sequence.ShouldBe(99);
    }

    [Fact]
    public async Task does_not_match_a_projection_whose_name_is_a_prefix()
    {
        await seedProgression(
            (ShardName.Compose("Orders"), 10),
            (ShardName.Compose("OrdersHistory"), 5));

        (await read("Orders", null))!.Sequence.ShouldBe(10);
        (await read("OrdersHistory", null))!.Sequence.ShouldBe(5);
    }
}
