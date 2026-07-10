using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace CoreTests;

// #4915, the near side of the #4874 case-B chain.
//
// IHost.Dispose() does not stop hosted services -- only StopAsync() does. So `using var host = ...`, and any
// teardown that disposes without a graceful stop, tears the container down underneath a running projection
// coordinator. The container disposes the DocumentStore and its owned NpgsqlDataSource, and the HotCold
// leadership loop keeps polling AdvisoryLock.TryAttainLockAsync against a pool that is already gone.
//
// Before the fix, measured on this exact shape: a cold node disposed without StopAsync produced 210
// disposed-pool ObjectDisposedExceptions in the 1.5 seconds after teardown, and kept going for the life of
// the process (~140/s). The same host stopped with StopAsync() first produced zero. That asymmetry is the
// bug: ProjectionCoordinator had no disposal at all, so the only thing that ever drained its loop was a
// graceful stop.
//
// The fix makes ProjectionCoordinator IAsyncDisposable/IDisposable, draining the loop and releasing the
// advisory locks. The coordinator is constructed from an IDocumentStore, so the container necessarily creates
// the store first and disposes it last -- the coordinator drains while its data source is still alive.
//
// Weasel 9.16.3 (weasel#354) independently makes a disposed data source terminal inside
// AdvisoryLock.TryAttainLockAsync, which drops the unfixed count from 210 to ~9 (one poll's worth of
// async-frame rethrows) by letting ProjectionCoordinatorBase's terminate-on-ObjectDisposedException catch
// finally fire. The two fixes are independent: either one alone brings this test to zero. This one keeps the
// loop from outliving its data source; that one keeps a loop that does from churning.
[Collection("integration")]
public class Bug_4915_coordinator_disposal_drain
{
    private readonly ITestOutputHelper _output;

    public Bug_4915_coordinator_disposal_drain(ITestOutputHelper output)
    {
        _output = output;
    }

    // CoreTests runs serially ([assembly: CollectionBehavior(DisableTestParallelization = true)]), but other
    // tests still produce disposed-pool ObjectDisposedExceptions of their own: leaked background work (daemon
    // StopAllAsync, schema migrations, session reads), and -- benignly -- AdvisoryLock.DisposeAsync releasing a
    // lock handle whose data source already went away, which Weasel swallows. FirstChanceException is an
    // AppDomain-wide hook, so a bare "zero disposed-pool aborts" assertion counts all of that and flakes. That
    // is why Bug_4874_coordinator_drain_ordering fails in a full run but passes alone.
    //
    // Attribute each abort to the leadership poll specifically -- TryAttainLockAsync re-opening a dead pool is
    // the bug, and lock *disposal* touching one is not. The exception's own StackTrace is not populated at
    // first-chance time, so walk the throwing thread's live stack.
    private static bool IsAdvisoryLockPollAbort(Exception exception)
    {
        if (exception is not ObjectDisposedException ode) return false;

        if (ode.ObjectName?.Contains("PoolingDataSource") != true &&
            !ode.Message.Contains("PoolingDataSource"))
        {
            return false;
        }

        return new StackTrace(false).ToString().Contains("TryAttainLockAsync", StringComparison.Ordinal);
    }

    [Fact]
    public async Task disposing_a_cold_node_without_stopasync_does_not_poll_the_disposed_datasource()
    {
        const string schema = "bug4915_coordinator_disposal";

        var aborts = new ConcurrentQueue<string>();
        var counting = false;

        void handler(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (!Volatile.Read(ref counting)) return;

            if (IsAdvisoryLockPollAbort(e.Exception))
            {
                aborts.Enqueue(e.Exception.ToString());
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += handler;
        try
        {
            // Hot node wins the single advisory lock and holds it, so the cold node below stays cold and keeps
            // re-polling (and therefore re-opening) to try to attain it.
            using var hot = BuildHost(schema);
            await hot.StartAsync();
            await Task.Delay(500);

            var cold = BuildHost(schema);
            await cold.StartAsync();

            // Let the cold node lose the lock a few times, so its poll loop is definitely live.
            await Task.Delay(400);

            // The shape under test: dispose with NO graceful stop, exactly as `using var host = ...` does.
            // This disposes the container, and with it the DocumentStore's owned NpgsqlDataSource.
            cold.Dispose();

            // Everything from here on is the churn window. A coordinator that outlived its data source
            // re-opens the dead pool on every LeadershipPollingTime tick (50ms below), so 1.5s of silence
            // is a meaningful signal: pre-fix this window held 210 aborts.
            Volatile.Write(ref counting, true);
            await Task.Delay(1500);
            Volatile.Write(ref counting, false);

            await hot.StopAsync();
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= handler;
        }

        foreach (var abort in aborts)
        {
            _output.WriteLine(abort);
        }

        aborts.Count.ShouldBe(0,
            $"Expected no disposed-pool aborts on the AdvisoryLock poll path after disposing the cold node, but saw " +
            $"{aborts.Count}. The projection coordinator's leadership loop outlived the data source the " +
            "container disposed underneath it (see #4915).");
    }

    [Fact]
    public async Task disposal_drains_a_loop_that_resumeasync_restarted_after_a_stop()
    {
        const string schema = "bug4915_resume_then_dispose";

        var aborts = new ConcurrentQueue<string>();
        var counting = false;

        void handler(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (!Volatile.Read(ref counting)) return;

            if (IsAdvisoryLockPollAbort(e.Exception))
            {
                aborts.Enqueue(e.Exception.ToString());
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += handler;
        try
        {
            using var hot = BuildHost(schema);
            await hot.StartAsync();
            await Task.Delay(500);

            var cold = BuildHost(schema);
            await cold.StartAsync();
            await Task.Delay(200);

            // Stop, then Resume: the coordinator builds a FRESH leadership loop. Disposal has to drain that
            // one, not conclude from a stale "already stopped" flag that there is nothing left to do. Use
            // StopAsync (not PauseAsync) because that is the call any such flag would be set from.
            var coordinator = cold.Services.GetRequiredService<IProjectionCoordinator>();
            await coordinator.StopAsync(CancellationToken.None);
            await coordinator.ResumeAsync();
            await Task.Delay(300);

            cold.Dispose();

            Volatile.Write(ref counting, true);
            await Task.Delay(1500);
            Volatile.Write(ref counting, false);

            await hot.StopAsync();
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= handler;
        }

        foreach (var abort in aborts)
        {
            _output.WriteLine(abort);
        }

        aborts.Count.ShouldBe(0,
            $"Expected no aborts after disposing a coordinator whose loop had been restarted by ResumeAsync, " +
            $"but saw {aborts.Count} (see #4915).");
    }

    private static IHost BuildHost(string schema)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                    {
                        // Connection-string overload => Marten OWNS the NpgsqlDataSource, so container disposal
                        // really does take the pool down with it.
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = schema;
                        opts.DisableNpgsqlLogging = true;

                        // Poll aggressively so an undrained loop shows up immediately.
                        opts.Projections.LeadershipPollingTime = 50;

                        opts.Projections.Add(new Bug4915CounterProjection(), ProjectionLifecycle.Async);
                    })
                    // HotCold => a single advisory lock, so the second node stays cold and keeps polling.
                    .AddAsyncDaemon(DaemonMode.HotCold);
            })
            .Build();
    }
}

public record Bug4915Incremented;

public class Bug4915Counter
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

// Convention-based projection subclass => must be top-level + partial so the JasperFx source generator emits
// its dispatcher (there is no runtime Apply/Create fallback).
public partial class Bug4915CounterProjection: SingleStreamProjection<Bug4915Counter, Guid>
{
    public void Apply(Bug4915Counter snapshot, Bug4915Incremented _)
    {
        snapshot.Count++;
    }
}
