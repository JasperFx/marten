using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.HighWater;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Daemon;

// Covers the IDaemonWakeup contract implementation in
// Marten.Events.Daemon.HighWater.PostgresqlListenWakeup.
//
// The wakeup opens a dedicated NpgsqlConnection that LISTENs on its
// channel, plus a Task.Run loop that drives Npgsql's connection.WaitAsync
// to dispatch incoming NOTIFYs. WaitAsync() then awaits a SemaphoreSlim
// the dispatcher releases.
//
// The tests fire a `pg_notify(...)` from a SEPARATE connection (the
// production flow runs pg_notify inside the event-append transaction;
// from the wakeup's point of view it's indistinguishable from any other
// LISTEN/NOTIFY signal). We then assert the wakeup unblocks well before
// the wait timeout — i.e. the listener wired the signal up correctly.
public class postgres_listen_notify_wakeup_tests
{
    [Fact]
    public async Task wait_async_returns_quickly_when_a_pg_notify_arrives()
    {
        var dataSource = NpgsqlDataSource.Create(ConnectionSource.ConnectionString);
        try
        {
            using var wakeup = new PostgresqlListenWakeup(
                dataSource,
                NullLogger.Instance,
                channel: "marten_test_listen_notify_wakeup");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            // Prime the LISTEN connection so the receiveLoop is up and dispatching
            // before we fire the NOTIFY. Without this prime the NOTIFY can arrive
            // before LISTEN registers and is dropped. We use the long-lived test
            // CT (not a short timeout) so the prime doesn't tear the listener
            // down before we get to the real wait.
            await wakeup.WaitAsync(TimeSpan.FromMilliseconds(200), cts.Token);

            var waitTask = wakeup.WaitAsync(TimeSpan.FromSeconds(10), cts.Token);

            // Tiny grace period so the wait task is parked on the semaphore before
            // pg_notify fires — WaitAsync drains accumulated signals at the top,
            // so a notify that lands during the drain would be lost.
            await Task.Delay(50, cts.Token);

            await using (var notifier = await dataSource.OpenConnectionAsync(cts.Token))
            await using (var cmd = notifier.CreateCommand())
            {
                cmd.CommandText = "select pg_notify('marten_test_listen_notify_wakeup', '')";
                await cmd.ExecuteNonQueryAsync(cts.Token);
            }

            var sw = Stopwatch.StartNew();
            await waitTask;
            sw.Stop();

            sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2),
                $"WaitAsync should unblock soon after the NOTIFY — took {sw.ElapsedMilliseconds}ms");
        }
        finally
        {
            await dataSource.DisposeAsync();
        }
    }

    public record LnInc(int N);

    public class LnCount
    {
        public Guid Id { get; set; }
        public int Total { get; set; }
    }

    public partial class LnProjection: SingleStreamProjection<LnCount, Guid>
    {
        public override LnCount Evolve(LnCount? snapshot, Guid id, IEvent e)
        {
            snapshot ??= new LnCount { Id = id };
            if (e.Data is LnInc inc) snapshot.Total += inc.N;
            return snapshot;
        }
    }

    [Fact]
    public async Task daemon_wakes_up_through_pg_notify_when_flag_is_on()
    {
        // End-to-end smoke: flip the new opt-in flag, append a single event,
        // and assert the async projection catches up promptly via the
        // LISTEN/NOTIFY signal (rather than waiting on the slow poll cycle).
        // The point isn't to nail down an exact wakeup latency — it's to
        // pin the wiring: NotifyEventAppendedOperation actually fires
        // pg_notify in the append transaction, PostgresqlListenWakeup
        // actually gets a signal, and the daemon advances. A correctness-only
        // assertion ("the projection processes the event before the test
        // CT fires") sidesteps timing flakes.
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddMarten(m =>
                {
                    m.Connection(ConnectionSource.ConnectionString);
                    m.DatabaseSchemaName = "bug4183_listen_notify";
                    m.Events.UseListenNotifyForEventAppends = true;
                    m.Projections.Add<LnProjection>(ProjectionLifecycle.Async);

                    // Force the polling fallback to be slow so the test
                    // only completes if LISTEN/NOTIFY actually fired —
                    // otherwise we'd be measuring the poll cycle, not
                    // the wakeup wiring.
                    m.Projections.StaleSequenceThreshold = TimeSpan.FromSeconds(30);
                }).AddAsyncDaemon(DaemonMode.Solo);
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<LnCount>(streamId, new LnInc(7));
            await session.SaveChangesAsync();
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        LnCount? loaded = null;
        while (!cts.IsCancellationRequested)
        {
            await using var query = store.QuerySession();
            loaded = await query.LoadAsync<LnCount>(streamId, cts.Token);
            if (loaded is { Total: 7 }) break;
            await Task.Delay(100, cts.Token);
        }

        loaded.ShouldNotBeNull();
        loaded!.Total.ShouldBe(7);
    }

    [Fact]
    public async Task wait_async_returns_when_timeout_elapses_without_a_notification()
    {
        // Sanity check the timeout path — no notification fires, so WaitAsync
        // returns when the per-call timeout elapses (the safety-net fallback
        // that keeps the daemon polling at the configured pace if the
        // listener stays silent).
        var dataSource = NpgsqlDataSource.Create(ConnectionSource.ConnectionString);
        try
        {
            using var wakeup = new PostgresqlListenWakeup(
                dataSource,
                NullLogger.Instance,
                channel: "marten_test_listen_notify_wakeup_silent");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var sw = Stopwatch.StartNew();
            await wakeup.WaitAsync(TimeSpan.FromMilliseconds(300), cts.Token);
            sw.Stop();

            sw.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
            sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await dataSource.DisposeAsync();
        }
    }
}
