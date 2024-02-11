using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing;

public class HotCold_leadership_election: DaemonContext
{
    public HotCold_leadership_election(ITestOutputHelper output): base(output)
    {
    }

    // WATCH OUT. These tests will all pass one at a time, but don't play nicely with any others

    [Fact]
    public async Task detect_high_water_mark_when_running_after_events_are_posted()
    {
        NumberOfStreams = 10;

        Logger.LogDebug($"The expected high water mark at the end is " + NumberOfEvents);

        await PublishSingleThreaded();

        using var host = await StartDaemonInHotColdMode();

        var daemon = host.Daemon();
        var tracker = daemon.Tracker;
        await tracker.WaitForHighWaterMark(NumberOfEvents, 15.Seconds());

        tracker.HighWaterMark.ShouldBe(NumberOfEvents);

        daemon.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public async Task end_to_end_with_events_already_published()
    {
        NumberOfStreams = 10;

        Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

        var host = await StartDaemonInHotColdMode();
        var waiter = host.Daemon().Tracker.WaitForShardState("TripCustomName:All", NumberOfEvents, 15.Seconds());

        await PublishSingleThreaded();

        await waiter;

        await CheckAllExpectedAggregatesAgainstActuals();
    }

    [Fact]
    public async Task end_to_end_with_events_already_published_several_daemons()
    {
        NumberOfStreams = 10;

        Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

        var host = await StartDaemonInHotColdMode();

        await assertIsRunning(host, 5.Seconds());

        var host2 = await StartAdditionalDaemonInHotColdMode();

        var waiter = host.Daemon().Tracker.WaitForShardState("TripCustomName:All", NumberOfEvents, 30.Seconds());

        await PublishSingleThreaded();

        await waiter;

        await CheckAllExpectedAggregatesAgainstActuals();
    }

    private async Task assertIsRunning(IHost host, TimeSpan timeout)
    {
        var daemon = host.Daemon();

        if (daemon.IsRunning) return;

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        while (stopwatch.Elapsed < timeout && !daemon.IsRunning)
        {
            await Task.Delay(200.Milliseconds());
        }

        stopwatch.Stop();
        daemon.IsRunning.ShouldBeTrue();
    }

    private async Task<ProjectionDaemon> findRunningDaemon(params IHost[] hosts)
    {
        var timeout = 5.Seconds();

        var stopwatch = new Stopwatch();
        stopwatch.Start();

        while (stopwatch.Elapsed < timeout)
        {
            var host = hosts.SingleOrDefault(x => x.Daemon().IsRunning);
            if (host != null) return host.Daemon();
            await Task.Delay(200.Milliseconds());
        }

        stopwatch.Stop();
        throw new Exception("Could not determine that a Daemon was started in the time allotted");
    }

    [Fact]
    public async Task one_additional_node_will_take_over_leadership_mechanics()
    {
        var host1 = await StartDaemonInHotColdMode();

        await assertIsRunning(host1, 5.Seconds());

        var host2 = await StartAdditionalDaemonInHotColdMode();
        await Task.Delay(500.Milliseconds());

        host1.Daemon().IsRunning.ShouldBeTrue();
        host2.Daemon().IsRunning.ShouldBeFalse();

        await host1.StopAsync();

        await assertIsRunning(host2, 5.Seconds());
    }

    [Fact]
    public async Task spin_up_several_daemons_and_fail_over()
    {
        var host = await StartDaemonInHotColdMode();

        await assertIsRunning(host, 3.Seconds());

        var others = new IHost[4];

        others[0] = await StartAdditionalDaemonInHotColdMode();
        others[1] = await StartAdditionalDaemonInHotColdMode();
        others[2] = await StartAdditionalDaemonInHotColdMode();
        others[3] = await StartAdditionalDaemonInHotColdMode();

        await host.StopAsync();

        var active = await findRunningDaemon(others);

        foreach (var other in others)
        {
            if (other.Daemon().Equals(active)) continue;

            other.Daemon().IsRunning.ShouldBeFalse();
        }
    }
}
