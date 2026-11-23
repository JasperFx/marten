using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Examples;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using Marten;
using Marten.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EventSourcingTests.Daemon;

/// <summary>
///     The code samples in docs/events/projections/async-daemon.md that no other test shows. Compiled only, never
///     run.
/// </summary>
public static class async_daemon_documentation_samples
{
    public static void transaction_scoped_leadership_lock(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();

        #region sample_async_daemon_transaction_scoped_leadership_lock

        builder.Services.AddMarten(opts =>
        {
            opts.Connection(connectionString);

            // Opt into the transaction-scoped leadership lock
            opts.Events.UseAdvisoryLockTransaction = true;
        })
        .AddAsyncDaemon(DaemonMode.HotCold);

        #endregion
    }

    public static void turn_off_leadership_lock_monitoring(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();

        #region sample_async_daemon_unmonitored_leadership_lock

        builder.Services.AddMarten(opts =>
        {
            opts.Connection(connectionString);

            // Opt OUT of leadership lock monitoring. Only do this knowing the failure mode above.
            opts.Events.UseMonitoredAdvisoryLock = false;
        })
        .AddAsyncDaemon(DaemonMode.HotCold);

        #endregion
    }

    public static async Task preview_and_advance_the_high_water_mark(DocumentStore store, CancellationToken ct)
    {
        #region sample_async_daemon_preview_high_water_advance

        IEventStore eventStore = store;
        var database = (await eventStore.AllDatabases()).Single();

        // Read-only: the stored mark, the committed ceiling, and how many sequence numbers would be skipped
        var previews = await eventStore.PreviewHighWaterAdvanceAsync(database, tenantId: null, ct);

        // Refuses (RefusedStaleExpectation) if the mark moved after the preview
        var results = await eventStore.AdvanceHighWaterMarkToLatestAsync(
            database, tenantId: null, expectedCurrentMark: previews[0].CurrentMark, ct);

        #endregion
    }

    public static void tenant_load_budget()
    {
        #region sample_async_daemon_tenant_load_budget

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));

            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;

            // The most events this store's tenant agents may hold on this node,
            // loaded but not yet committed. Zero or negative (the default) is unbounded
            opts.Projections.TenantLoadBudget = 2000;

            // Hand out no new pages once the process reaches this fraction of
            // the GC's high memory load threshold. 0.9 is the default
            opts.Projections.TenantLoadMemoryThreshold = 0.9;
        });

        #endregion
    }

    public static void listen_notify_for_event_appends()
    {
        var builder = Host.CreateApplicationBuilder();

        #region sample_async_daemon_listen_notify

        builder.Services.AddMarten(opts =>
        {
            opts.Connection("some connection string");

            // Off by default
            opts.Events.UseListenNotifyForEventAppends = true;
        });

        #endregion
    }

    public static async Task replay_or_discard_a_parked_identity(IDocumentStore store, Guid tripId,
        CancellationToken token)
    {
        #region sample_async_daemon_replay_or_discard_parked_identity

        var daemon = await store.BuildProjectionDaemonAsync();

        // After deploying the fix
        await daemon.ReplayParkedIdentityAsync("Trip", tenantId: null, tripId, token);

        // Or accept the loss: the events that still fail are dead-lettered, the rest are applied
        await daemon.DiscardParkedIdentityAsync("Trip", tenantId: null, tripId, token);

        #endregion
    }

    public static async Task why_a_shard_is_down(IDocumentStore store)
    {
        #region sample_async_daemon_reading_shard_failures

        var states = await store.Storage.Database.AllProjectionProgress();
        foreach (var state in states.Where(x => x.Failure != null))
        {
            // ApplyEvent, EventSerialization, UnknownEventType, ProgressionOutOfOrder,
            // ParkedLimitReached, or Other
            Console.WriteLine($"{state.ShardName}: {state.Failure!.Category} on {state.Failure.Event}");
        }

        #endregion
    }

    public static void extended_progression_heartbeat(StoreOptions opts)
    {
        #region sample_async_daemon_extended_progression_heartbeat

        opts.Events.EnableExtendedProgressionTracking = true;

        // Off by default. Any positive value restores the periodic flush at that cadence;
        // null, zero or negative keeps it off.
        opts.Projections.ExtendedProgressionHeartbeatInterval = TimeSpan.FromSeconds(30);

        #endregion
    }

    public static async Task return_stale_data_on_timeout(IDocumentSession session)
    {
        #region sample_async_daemon_return_stale_data_on_timeout

        // Wait up to 5 seconds for the projection to catch up, but if it cannot,
        // return the latest available (possibly stale) data instead of throwing.
        var latest = await session
            .QueryForNonStaleData<Trip>(5.Seconds(), NonStaleDataTimeoutMode.ReturnStaleData)
            .OrderByDescending(x => x.Started)
            .Take(10)
            .ToListAsync();

        #endregion
    }
}
