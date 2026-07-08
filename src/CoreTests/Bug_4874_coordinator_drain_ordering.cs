using System;
using System.Collections.Concurrent;
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

// #4874 (case B — the disposal-ORDERING storm, distinct from the tenancy async-disposal foundation in
// Bug_4874_async_tenancy_disposal). Reporter steve-ziegler confirmed
// (https://github.com/JasperFx/marten/issues/4874#issuecomment-4914607213):
//
//   * `await host.StopAsync()` IS awaited before disposal, and
//   * the aborting `OpenAsync` is PURELY the native HotCold projection coordinator's leadership poll
//     (40 async-stitched stack samples, zero Wolverine frames):
//
//       ProjectionCoordinatorBase.executeAsync
//        -> SingleTenantProjectionDistributor.TryAttainLockAsync
//           -> Weasel.Postgresql.AdvisoryLock.TryAttainLockAsync
//              -> Medallion.Threading.Postgres.PostgresDistributedLock.TryAcquireAsync
//                 -> Npgsql OpenAsync  ==> ObjectDisposedException 'Npgsql.PoolingDataSource'
//
//   * the aborts land ~160ms AFTER the owned NpgsqlDataSource is disposed, which itself runs AFTER
//     StopAsync() returned.
//
// Mechanism: on a HotCold *cold* (standby) node, every leadership poll re-opens a fresh connection from
// the data source to attempt `pg_try_advisory_xact_lock`, fails (the hot node holds the lock), and drops
// it. When a cold host is torn down while that poll is in flight, the owned NpgsqlDataSource is disposed
// out from under an OpenAsync the coordinator loop's StopAsync did not drain.
//
// The fix is upstream and is now consumed on master:
//   Priority 1 - Weasel 9.16.2 (weasel#349/#350): AdvisoryLock disposing-guard + swallow disposed-pool
//                ObjectDisposedException in TryAttainLockAsync (return false = "not attained during shutdown").
//   Priority 2 - JasperFx 2.24.1 (jasperfx#499/#500): ProjectionCoordinatorBase.executeAsync returns on
//                cancellation / treats ObjectDisposedException as terminal instead of re-polling.
//
// This test reproduces the abort in the reporter's shape (HotCold, connection-string / Marten-owned
// source, boot+teardown under an active cold-node poll) and asserts zero PoolingDataSource aborts. It
// failed on the pre-fix stack and passes now that Weasel 9.16.2 + JasperFx 2.24.1 are referenced.
[Collection("integration")]
public class Bug_4874_coordinator_drain_ordering
{
    private readonly ITestOutputHelper _output;

    public Bug_4874_coordinator_drain_ordering(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task cold_node_teardown_does_not_abort_open_against_disposed_datasource()
    {
        const string schema = "bug4874_coordinator_drain";

        // Count only the specific abort this bug produces: an OpenAsync against a disposed Npgsql pool.
        var aborts = new ConcurrentQueue<string>();
        void handler(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is ObjectDisposedException ode &&
                (ode.ObjectName?.Contains("PoolingDataSource") == true ||
                 ode.Message.Contains("PoolingDataSource")))
            {
                aborts.Enqueue(ode.ToString());
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += handler;
        try
        {
            // Hot node: boots once and wins the single advisory lock, then holds it for the whole test so
            // every cold node below stays cold and keeps re-polling (re-opening) to try to attain it.
            using var hot = BuildHost(schema);
            await hot.StartAsync();

            // Give the hot node a beat to attain the lock before the cold nodes start contending.
            await Task.Delay(250);

            // Boot + tear down a cold node repeatedly. Each teardown disposes the cold node's owned
            // NpgsqlDataSource while its leadership poll is actively opening connections to retry the lock.
            for (var i = 0; i < 20; i++)
            {
                var cold = BuildHost(schema);
                await cold.StartAsync();

                // Let the cold node's coordinator poll a few times (LeadershipPollingTime is 50ms below),
                // so an OpenAsync is very likely in flight when we dispose.
                await Task.Delay(200);

                // Reporter's exact teardown shape: await graceful stop, THEN dispose.
                await cold.StopAsync();
                cold.Dispose();
            }

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
            $"Expected no ObjectDisposedException('PoolingDataSource') aborts, but saw {aborts.Count}. " +
            "The cold-node leadership poll opened a connection against an already-disposed data source " +
            "during teardown (see #4874).");
    }

    private static IHost BuildHost(string schema)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                    {
                        // Connection-string overload => Marten OWNS the NpgsqlDataSource (OwnsDataSource == true),
                        // so the #4903/#4905 ownership guards do NOT skip disposal for this shape.
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = schema;
                        opts.DisableNpgsqlLogging = true;

                        // Poll aggressively so a cold node is almost always mid-OpenAsync at teardown.
                        opts.Projections.LeadershipPollingTime = 50;

                        // At least one async projection so the coordinator has a shard to contend for.
                        opts.Projections.Add(new Bug4874CounterProjection(), ProjectionLifecycle.Async);
                    })
                    // HotCold => single advisory lock; second+ nodes stay cold and re-poll (re-open) forever.
                    .AddAsyncDaemon(DaemonMode.HotCold);
            })
            .Build();
    }
}

public record Bug4874Incremented;

public class Bug4874Counter
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

// Convention-based projection subclass => must be top-level + partial so the JasperFx source generator
// emits its dispatcher (there is no runtime Apply/Create fallback).
public partial class Bug4874CounterProjection: SingleStreamProjection<Bug4874Counter, Guid>
{
    public void Apply(Bug4874Counter snapshot, Bug4874Incremented _)
    {
        snapshot.Count++;
    }
}
