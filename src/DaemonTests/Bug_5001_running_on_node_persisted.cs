using System;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Shouldly;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests;

public record Bug5001Event();

public class Bug5001Stream { public Guid Id { get; set; } }

public partial class Bug5001Projection: SingleStreamProjection<Bug5001Stream, Guid>
{
    public void Apply(Bug5001Event @event, Bug5001Stream projection) { }
}

// #5001 regression guard for the Marten half of the extended-progression running_on_node write path.
//
// The full symptom #5001 reported: under Wolverine-managed subscription distribution the sibling telemetry
// columns (heartbeat / agent_status) write fine, but running_on_node stays NULL forever. The end-to-end
// root cause and cross-repo fix live outside Marten (nothing stamped AssignedNodeNumber onto a
// runtime-published ShardState until the Wolverine distribution layer began doing so, carried on the
// JasperFx 2.33.1 ShardStateTracker seam). Marten itself owns exactly one link in that chain:
// WriteExtendedProgressionAsync must persist ShardState.RunningOnNode into the running_on_node column, and
// the async daemon must subscribe the ExtendedProgressionWriter to the tracker so a stamped node actually
// reaches that write.
//
// These tests pin that Marten link against the pinned JasperFx build, without needing Wolverine: they stamp
// the daemon's tracker exactly as the distribution layer does and assert what Marten persists on the
// progression row an async projection is already maintaining.
public class Bug_5001_running_on_node_persisted: DaemonContext
{
    public Bug_5001_running_on_node_persisted(ITestOutputHelper output): base(output)
    {
    }

    private const string Shard = "Bug5001Stream:All";

    private async Task<object?> readColumnAsync(string column)
    {
        await using var session = theStore.QuerySession();
        var raw = await session.Connection
            .CreateCommand(
                $"select {column} from {theStore.Events.DatabaseSchemaName}.mt_event_progression where name = :name")
            .With("name", Shard)
            .ExecuteScalarAsync();

        return raw is null or DBNull ? null : raw;
    }

    // The extended-progression write lands on a background block, so give it a beat to appear.
    private async Task<object?> pollColumnAsync(string column, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            var value = await readColumnAsync(column);
            if (value != null) return value;
            await Task.Delay(50);
        }

        return await readColumnAsync(column);
    }

    // A status transition carrying agent telemetry is persisted immediately (never throttled), and by this
    // point the shard's progression row already exists (the async projection created it as it caught up), so
    // the extended-progression function takes its UPDATE path. That is the deterministic way to land one
    // telemetry write without waiting on the ~10s heartbeat timer.
    private static ShardState telemetryTransition() => new(Shard, 10)
    {
        Action = ShardAction.Started,
        AgentStatus = "Running",
        LastHeartbeat = DateTimeOffset.UtcNow
    };

    private async Task<IProjectionDaemon> startCaughtUpDaemonAsync()
    {
        StoreOptions(x =>
        {
            x.Events.EnableExtendedProgressionTracking = true;
            x.Projections.Add(new Bug5001Projection(), ProjectionLifecycle.Async);
        });

        var daemon = await StartDaemon();

        await using (var session = theStore.LightweightSession())
        {
            for (var i = 0; i < 10; i++)
            {
                session.Events.Append(Guid.NewGuid(), new Bug5001Event());
            }

            await session.SaveChangesAsync();
        }

        // Let the shard advance so its mt_event_progression row exists for the extended function to update.
        await daemon.Tracker.WaitForShardState(new ShardState(Shard, 10), 30.Seconds());

        return daemon;
    }

    [Fact]
    public async Task running_on_node_is_persisted_when_the_tracker_carries_an_assigned_node()
    {
        var daemon = await startCaughtUpDaemonAsync();

        // #5001: the Wolverine-managed subscription-distribution layer stamps the owning node number onto the
        // daemon's tracker (JasperFx 2.33.1 ShardStateTracker seam). Do the same here so every published
        // ShardState carries AssignedNodeNumber; ExtendedProgressionWriter then carries that into the
        // running_on_node column via WriteExtendedProgressionAsync.
        daemon.Tracker.AssignedNodeNumber = 7;

        await daemon.Tracker.PublishAsync(telemetryTransition());

        var node = await pollColumnAsync("running_on_node", 10.Seconds());
        Convert.ToInt32(node).ShouldBe(7);

        // the sibling telemetry columns write on the very same code path
        (await readColumnAsync("agent_status")).ShouldBe("Running");
        (await readColumnAsync("heartbeat")).ShouldNotBeNull();
    }

    [Fact]
    public async Task running_on_node_stays_null_when_no_distribution_layer_assigns_a_node()
    {
        var daemon = await startCaughtUpDaemonAsync();

        // No AssignedNodeNumber stamped (defaults to 0), exactly like a single-node / HotCold daemon that owns
        // no managed node assignment. This characterizes the #5001 symptom: the sibling telemetry columns
        // still write, but running_on_node has nothing to carry and correctly stays NULL. The gap is the
        // absent distribution-layer stamp, not Marten's write path.
        await daemon.Tracker.PublishAsync(telemetryTransition());

        // agent_status is written on the same call, so once it lands the row's telemetry has been refreshed
        // and running_on_node has had its chance to be written.
        (await pollColumnAsync("agent_status", 10.Seconds())).ShouldBe("Running");
        (await readColumnAsync("running_on_node")).ShouldBeNull();
    }
}
