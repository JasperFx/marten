using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon.Coordination;
using Marten.Events.Projections;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace DaemonTests;

public record HotColdStopEvent();

public class HotColdStopDoc { public long Id { get; set; } }

// One projection instance per shard.
public partial class HotColdStopEventProjection: EventProjection
{
    public HotColdStopEventProjection(int index)
    {
        Name = $"HotColdStop{index}";
    }

    public void Project(IEvent<HotColdStopEvent> e, IDocumentOperations ops)
    {
        ops.Store(new HotColdStopDoc { Id = e.Sequence });
    }
}

/// <summary>
/// #5567. Stopping a HotCold coordinator that held many projection locks could stall for a whole
/// 60-second monitor window -- or two, one per shared connection -- before the locks were released,
/// with nothing logged.
/// </summary>
/// <remarks>
/// The cause is in Patched.DistributedLock.Core 1.0.11, and both halves of it are visible in the
/// shipped IL. <c>MultiplexedConnectionLock.Handle.DisposeAsync</c> disposes the connection's
/// monitoring handle BEFORE <c>ReleaseAsync</c>, which is what needs the shared connection to run
/// <c>pg_advisory_unlock</c>. And <c>ConnectionMonitor.AcquireConnectionLockAsync</c> only fires the
/// wake-up that interrupts the monitor's one-minute wait while
/// <c>HasRegisteredMonitoringHandlesNoLock</c> is true. The monitor worker committed to monitoring
/// from a count the releaser has since decremented, so the last release on a connection fires
/// nothing and its own two-second retry loop re-evaluates that same false condition until the window
/// expires.
/// <para>
/// Marten's own <see cref="AdvisoryLock" /> therefore releases concurrently and bounds the wait --
/// see <see cref="AdvisoryLock.DisposeAsync" />. <see cref="StoreOptions" />'
/// <c>Projections.StopAndDrainTimeout</c> is the bound, which is why this test sets it to 2s and
/// then allows 5s: the point is that the stop is bounded at all, not that it is instant.
/// </para>
/// <para>
/// Worth knowing if this ever regresses: the stall was probabilistic, reproducing on iteration 10 of
/// one run and iteration 1 of the next, and not at all in two others. So a passing run of a handful
/// of iterations never proved much -- which is why this one runs 30.
/// </para>
/// </remarks>
public class Bug_5567_hotcold_stop_stalls_behind_monitored_lock: DaemonContext
{
    public Bug_5567_hotcold_stop_stalls_behind_monitored_lock(ITestOutputHelper output): base(output)
    {
    }

    private const int Iterations = 30;

    // More shards mean more shared-connection lock releases, making the stall easier to trigger.
    // 24 is a reproduction aid, not a known minimum; one shard passing does not rule out the bug.
    private const int Shards = 24;

    [Theory]
    [InlineData(false, true)]  // the 9.41+ defaults -- the combination that used to stall
    [InlineData(false, false)] // session-scoped lock without monitoring
    [InlineData(true, true)]   // the pre-9.41 transaction-scoped lock
    public async Task stopping_a_hotcold_coordinator_releases_its_locks_promptly(bool useAdvisoryLockTransaction, bool useMonitoredAdvisoryLock)
    {
        StoreOptions(x =>
        {
            for (var shard = 0; shard < Shards; shard++)
            {
                x.Projections.Add(new HotColdStopEventProjection(shard), ProjectionLifecycle.Async);
            }

            x.Projections.AsyncMode = DaemonMode.HotCold;
            x.Projections.StopAndDrainTimeout = 2.Seconds();
            // Exercise HasLock and activate monitoring before stopping.
            x.Projections.LeadershipPollingTime = 100;

            x.Events.UseAdvisoryLockTransaction = useAdvisoryLockTransaction;
            x.Events.UseMonitoredAdvisoryLock = useMonitoredAdvisoryLock;
        });

        var slowest = TimeSpan.Zero;
        var slowestIteration = 0;

        for (var i = 1; i <= Iterations; i++)
        {
            var coordinator = new ProjectionCoordinator(theStore, NullLogger<ProjectionCoordinator>.Instance);
            await coordinator.StartAsync(CancellationToken.None);

            // Allow time to acquire locks and start monitoring.
            await Task.Delay(1500, TestContext.Current.CancellationToken);

            var stopwatch = Stopwatch.StartNew();
            await coordinator.StopAsync(CancellationToken.None);
            stopwatch.Stop();

            if (stopwatch.Elapsed > slowest)
            {
                slowest = stopwatch.Elapsed;
                slowestIteration = i;
            }

            stopwatch.Elapsed.ShouldBeLessThan(5.Seconds(),
                $"StopAsync stalled for {stopwatch.Elapsed.TotalSeconds:F1} s on iteration {i} of {Iterations}");
        }

        _output.WriteLine(
            $"slowest StopAsync over {Iterations} iterations: {slowest.TotalMilliseconds:F0} ms (iteration {slowestIteration})");
    }
}
