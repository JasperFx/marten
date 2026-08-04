using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Progress;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Bugs;

/// <summary>
/// #5172 — both <c>ReadProjectionProgressAsync</c> overloads hardcoded <c>AgentStatus</c> and
/// <c>LastHeartbeat</c> to null and never selected the columns, on the rationale (jasperfx#519) that no
/// daemon path wrote them. That stopped being true at jasperfx#537: <c>ExtendedProgressionWriter</c>
/// populates both on every flush, and the row-scan path (<c>AllProjectionProgress</c>) reads them back
/// correctly. Only these targeted per-cell reads — the ones a monitor is meant to use instead of pulling
/// every row — were left returning a placeholder that reads exactly like a fact.
/// </summary>
public class Bug_5172_read_progress_extended_columns: OneOffConfigurationsContext, IAsyncLifetime
{
    private static readonly DateTimeOffset TheHeartbeat =
        new(2026, 8, 4, 12, 30, 15, TimeSpan.Zero);

    public override ValueTask DisposeAsync()
    {
        Dispose();
        return base.DisposeAsync();
    }

    private async Task withExtendedTracking(bool enabled)
    {
        StoreOptions(x => x.Events.EnableExtendedProgressionTracking = enabled);
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
        await theStore.EnsureStorageExistsAsync(typeof(IEvent));
    }

    private async Task seedProgression(params (ShardName name, long sequence)[] rows)
    {
        await using var session = theStore.LightweightSession();
        foreach (var (name, sequence) in rows)
        {
            session.QueueOperation(new InsertProjectionProgress(theStore.Events, new EventRange(name, sequence)));
        }

        await session.SaveChangesAsync();
    }

    private Task writeTelemetry(ShardName name, string status, DateTimeOffset heartbeat)
    {
        var state = new ShardState(name, 0) { AgentStatus = status, LastHeartbeat = heartbeat };
        return ((IEventDatabase)theStore.Tenancy.Default.Database)
            .WriteExtendedProgressionAsync([state], CancellationToken.None);
    }

    private ValueTask<ProjectionProgressRow?> read(string projectionName, string? tenantId) =>
        ((IEventDatabase)theStore.Tenancy.Default.Database)
        .ReadProjectionProgressAsync(projectionName, tenantId, CancellationToken.None);

    private ValueTask<ProjectionProgressRow?> read(ShardName name) =>
        ((IEventDatabase)theStore.Tenancy.Default.Database)
        .ReadProjectionProgressAsync(name, CancellationToken.None);

    [Fact]
    public async Task the_projection_name_overload_reads_the_persisted_status_and_heartbeat()
    {
        await withExtendedTracking(true);

        var shard = ShardName.Compose("Orders");
        await seedProgression((shard, 42));
        await writeTelemetry(shard, "Running", TheHeartbeat);

        var row = await read("Orders", null);

        row.ShouldNotBeNull();
        row.Sequence.ShouldBe(42);
        row.AgentStatus.ShouldBe("Running");
        row.LastHeartbeat.ShouldNotBeNull();
        row.LastHeartbeat.Value.ToUniversalTime().ShouldBe(TheHeartbeat);
    }

    [Fact]
    public async Task the_exact_shard_name_overload_reads_the_persisted_status_and_heartbeat()
    {
        await withExtendedTracking(true);

        var shard = ShardName.Compose("Orders", tenantId: "tenant1");
        await seedProgression((shard, 17));
        await writeTelemetry(shard, "Paused", TheHeartbeat);

        var row = await read(shard);

        row.ShouldNotBeNull();
        row.Sequence.ShouldBe(17);
        row.TenantId.ShouldBe("tenant1");
        row.AgentStatus.ShouldBe("Paused");
        row.LastHeartbeat!.Value.ToUniversalTime().ShouldBe(TheHeartbeat);
    }

    // The version-collapsing overload picks the newest version's row -- the telemetry it reports has to
    // come from that same row, not from whichever candidate happened to be read first.
    [Fact]
    public async Task the_winning_version_supplies_the_telemetry()
    {
        await withExtendedTracking(true);

        var v1 = ShardName.Compose("Orders");
        var v3 = ShardName.Compose("Orders", version: 3);
        await seedProgression((v1, 10), (v3, 40));

        await writeTelemetry(v1, "Stopped", TheHeartbeat.AddHours(-5));
        await writeTelemetry(v3, "Running", TheHeartbeat);

        var row = await read("Orders", null);

        row!.Sequence.ShouldBe(40);
        row.AgentStatus.ShouldBe("Running");
        row.LastHeartbeat!.Value.ToUniversalTime().ShouldBe(TheHeartbeat);
    }

    // A row that exists but has never been decorated still reads back cleanly -- the columns are simply
    // NULL, which is a fact ("nothing reported yet") rather than the old unconditional placeholder.
    [Fact]
    public async Task an_undecorated_row_reports_nulls()
    {
        await withExtendedTracking(true);

        var shard = ShardName.Compose("Orders");
        await seedProgression((shard, 42));

        var row = await read("Orders", null);

        row!.Sequence.ShouldBe(42);
        row.AgentStatus.ShouldBeNull();
        row.LastHeartbeat.ShouldBeNull();
    }

    // With extended tracking off the columns are not on the table at all, so both overloads must keep
    // selecting the narrow list rather than failing on an undefined column.
    [Fact]
    public async Task without_extended_tracking_both_overloads_still_work_and_report_nulls()
    {
        await withExtendedTracking(false);

        var shard = ShardName.Compose("Orders");
        await seedProgression((shard, 42));

        var byName = await read("Orders", null);
        byName!.Sequence.ShouldBe(42);
        byName.AgentStatus.ShouldBeNull();
        byName.LastHeartbeat.ShouldBeNull();

        var byShard = await read(shard);
        byShard!.Sequence.ShouldBe(42);
        byShard.AgentStatus.ShouldBeNull();
        byShard.LastHeartbeat.ShouldBeNull();
    }
}
