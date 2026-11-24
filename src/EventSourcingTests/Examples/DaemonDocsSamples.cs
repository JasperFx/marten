using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using Marten;
using IProjectionCoordinator = Marten.Events.Daemon.Coordination.IProjectionCoordinator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventSourcingTests.Examples;

// Samples for docs/events/projections/async-daemon.md. These compile, but are not tests.
public static class DaemonDocsSamples
{
    public static void ConfigureDaemonSettings(StoreOptions opts)
    {
        #region sample_async_daemon_settings_reference

        // Every value below is the default, except DaemonLockId
        opts.Projections.FastPollingTime = 250.Milliseconds();
        opts.Projections.SlowPollingTime = 1.Seconds();
        opts.Projections.StaleSequenceThreshold = 3.Seconds();
        opts.Projections.UseTransactionEvidenceForGapSkipping = true;
        opts.Projections.HealthCheckPollingTime = 5.Seconds();
        opts.Projections.HighWaterStalenessThreshold = 30.Seconds();
        opts.Projections.HighWaterProgressStallThreshold = 2.Minutes();

        // HotCold leadership. LeadershipPollingTime is in milliseconds
        opts.Projections.LeadershipPollingTime = 5000;
        opts.Projections.AgentPauseTime = 1.Seconds();

        // The default is 4444. Give every application that shares a database its own value
        opts.Projections.DaemonLockId = 5150;

        // Parking guard rails and replay batching
        opts.Projections.Errors.ParkedTripMinimum = 20;
        opts.Projections.Errors.ParkedTripFraction = 0.5;
        opts.Projections.Errors.ParkedReplayBatchSize = 10;

        #endregion
    }

    #region sample_async_daemon_runtime_controls

    public static async Task ControlTheRunningDaemon(IHost host, CancellationToken token)
    {
        var coordinator = host.Services.GetRequiredService<IProjectionCoordinator>();

        // Stop the coordinator from starting shards again, and stop every running shard
        // on every database. Nothing restarts until ResumeAsync()
        await coordinator.PauseAsync();

        foreach (var daemon in await coordinator.AllDaemonsAsync())
        {
            // Running, Paused or Stopped. A shard this daemon doesn't hold is Stopped
            Console.WriteLine($"Trip:All is {daemon.StatusFor("Trip:All")}");
        }

        await coordinator.ResumeAsync();
    }

    public static async Task WorkWithOneDaemon(IProjectionDaemon daemon, CancellationToken token)
    {
        // Stop every running shard of one projection, matched by name. Pass a
        // tenant id to stop only that tenant's shards
        await daemon.PauseShardAsync("Trip", tenantId: null, token);

        // Restart the high water agent's poll loop if it has stopped cycling.
        // This never moves the high water mark
        if (daemon.IsHighWaterStale)
        {
            await daemon.RestartHighWaterAgentAsync(token);
        }

        // The identities parked in this daemon's database, oldest first
        var parked = await daemon.FetchParkedIdentitiesAsync("Trip", tenantId: null, token);
        Console.WriteLine($"{parked.Count} Trip identities are parked");

        // Replay the five identities parked first, in the shard that runs them here
        var results = await daemon.ReplayParkedIdentitiesAsync("Trip", tenantId: null,
            ParkedSelection.OldestFirst(5), token);

        foreach (var result in results)
        {
            Console.WriteLine($"{result.Identity}: {result.Outcome}");
        }
    }

    #endregion

    #region sample_async_daemon_correct_progress

    public static async Task CorrectProgression(IDocumentStore store, CancellationToken token)
    {
        // The shard names of every async projection, e.g. "Trip:All"
        foreach (var shardName in store.Advanced.AllAsyncProjectionShardNames())
        {
            Console.WriteLine(shardName.Identity);
        }

        // Every database of the store
        await store.Advanced.TryCorrectProgressInDatabaseAsync(token);

        // Or only the database that holds one tenant
        await store.Advanced.TryCorrectProgressInDatabaseAsync("tenant1", token);
    }

    #endregion
}
