using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Subscriptions;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace Marten.AspNetCore.Testing;

public interface IBug5705Store: IDocumentStore;

public class Bug5705Event;

public class Bug5705Subscription: SubscriptionBase
{
    public override Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
        IDocumentOperations operations, CancellationToken cancellationToken)
    {
        return Task.FromResult(NullChangeListener.Instance);
    }
}

/// <summary>
/// marten#5705: <c>AddMartenHighWaterHealthCheck()</c> only knew the default store. An application that
/// registered its stores only through <c>AddMartenStore&lt;TStore&gt;()</c> got a check that threw on its
/// first probe, because nothing registers <see cref="IDocumentStore" /> there, and <c>autoRestart</c>
/// only ever asked the unmarked coordinator.
///
/// <para>
/// The hosts here are built and never started, so no daemon runs and a seeded high-water row stays
/// where it was put. That is the stalled agent the check exists to report.
/// </para>
/// </summary>
public class high_water_health_check_for_ancillary_stores
{
    private const string AncillarySchema = "bug_5705_ancillary";
    private const string DefaultSchema = "bug_5705_default";

    private static void configureAncillary(IServiceCollection services) =>
        services.AddMartenStore<IBug5705Store>(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = AncillarySchema;
                opts.Projections.Subscribe(new Bug5705Subscription());
            })
            .AddAsyncDaemon(DaemonMode.Solo);

    private static IHost buildHost(Action<IServiceCollection> configure)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(configure)
            .Build();
    }

    // Puts a stalled agent on the store: events past a high-water row that will not move
    private static async Task stallAsync(IDocumentStore store, long mark, int events = 5)
    {
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using (var session = store.LightweightSession())
        {
            for (var i = 0; i < events; i++)
            {
                session.Events.StartStream(Guid.NewGuid(), new Bug5705Event());
            }

            await session.SaveChangesAsync();
        }

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.CreateCommand(
                $"insert into {store.Options.Events.DatabaseSchemaName}.mt_event_progression (name, last_seq_id, last_updated) " +
                $"values ('HighWaterMark', {mark}, now()) " +
                "on conflict (name) do update set last_seq_id = excluded.last_seq_id")
            .ExecuteNonQueryAsync();
    }

    // The gap heuristic needs two readings a threshold apart: the first notes the stuck mark, the
    // second finds it still stuck
    private static async Task<HealthReport> probeTwiceAsync(IHost host, TimeSpan threshold)
    {
        var health = host.Services.GetRequiredService<HealthCheckService>();
        await health.CheckHealthAsync();
        await Task.Delay(threshold + 100.Milliseconds());
        return await health.CheckHealthAsync();
    }

    [Fact]
    public async Task a_store_registered_only_through_AddMartenStore_can_be_probed()
    {
        using var host = buildHost(services =>
        {
            configureAncillary(services);
            services.AddHealthChecks().AddMartenHighWaterHealthCheck<IBug5705Store>();
        });

        var store = host.Services.GetRequiredService<IBug5705Store>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        var entry = report.Entries[HighWaterHealthCheckExtensions.HighWaterHealthCheckNameFor<IBug5705Store>()];
        entry.Exception.ShouldBeNull();
        entry.Status.ShouldBe(HealthStatus.Healthy);
        entry.Tags.ShouldContain(nameof(IBug5705Store));
    }

    /// <summary>
    /// The report, kept as the behaviour of the store-less overload: it still means the default store.
    /// </summary>
    [Fact]
    public async Task the_default_check_still_needs_the_default_store()
    {
        using var host = buildHost(services =>
        {
            configureAncillary(services);
            services.AddHealthChecks().AddMartenHighWaterHealthCheck();
        });

        // The health check service builds each check before running it, and a check it cannot build
        // throws out of the probe rather than reporting Unhealthy
        var failure = await Should.ThrowAsync<InvalidOperationException>(() =>
            host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync());
        failure.Message.ShouldContain(typeof(IDocumentStore).FullName!);
    }

    [Fact]
    public async Task a_stalled_mark_on_the_ancillary_store_is_reported()
    {
        var threshold = 200.Milliseconds();
        using var host = buildHost(services =>
        {
            configureAncillary(services);
            services.AddHealthChecks().AddMartenHighWaterHealthCheck<IBug5705Store>(staleThreshold: threshold);
        });

        await stallAsync(host.Services.GetRequiredService<IBug5705Store>(), mark: 1);

        var report = await probeTwiceAsync(host, threshold);

        var entry = report.Entries[HighWaterHealthCheckExtensions.HighWaterHealthCheckNameFor<IBug5705Store>()];
        entry.Status.ShouldBe(HealthStatus.Unhealthy);
        entry.Description.ShouldContain("stuck at 1");
    }

    [Fact]
    public async Task auto_restart_goes_through_the_ancillary_stores_own_coordinator()
    {
        var threshold = 200.Milliseconds();
        var coordinator = new RecordingCoordinator();
        using var host = buildHost(services =>
        {
            configureAncillary(services);

            // Registered last, so it is the one resolved for this store
            services.AddSingleton<Marten.Events.Daemon.Coordination.IProjectionCoordinator<IBug5705Store>>(
                coordinator);

            services.AddHealthChecks()
                .AddMartenHighWaterHealthCheck<IBug5705Store>(staleThreshold: threshold, autoRestart: true);
        });

        var store = host.Services.GetRequiredService<IBug5705Store>();
        await stallAsync(store, mark: 1);

        var report = await probeTwiceAsync(host, threshold);

        var entry = report.Entries[HighWaterHealthCheckExtensions.HighWaterHealthCheckNameFor<IBug5705Store>()];
        entry.Status.ShouldBe(HealthStatus.Unhealthy);
        coordinator.AskedFor.ShouldBe([store.Storage.Database.Identifier]);
    }

    /// <summary>
    /// Two stores in one database, both stalled at different marks: each check reads its own store. The
    /// staleness readings share one tracker, keyed by database identifier, which is the store's StoreName
    /// and so differs between the two even here.
    /// </summary>
    [Fact]
    public async Task each_store_keeps_its_own_settings_and_tracking()
    {
        var threshold = 200.Milliseconds();
        using var host = buildHost(services =>
        {
            services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = DefaultSchema;
                    opts.Projections.Subscribe(new Bug5705Subscription());
                })
                .AddAsyncDaemon(DaemonMode.Solo);

            configureAncillary(services);

            services.AddHealthChecks()
                .AddMartenHighWaterHealthCheck(staleThreshold: threshold)
                .AddMartenHighWaterHealthCheck<IBug5705Store>(staleThreshold: threshold);
        });

        await stallAsync(host.Services.GetRequiredService<IDocumentStore>(), mark: 1);
        await stallAsync(host.Services.GetRequiredService<IBug5705Store>(), mark: 2);

        var report = await probeTwiceAsync(host, threshold);

        report.Entries[nameof(HighWaterHealthCheckExtensions.HighWaterHealthCheck)]
            .Description.ShouldContain("stuck at 1");
        report.Entries[HighWaterHealthCheckExtensions.HighWaterHealthCheckNameFor<IBug5705Store>()]
            .Description.ShouldContain("stuck at 2");
    }

    [Fact]
    public async Task registering_an_ancillary_check_does_not_reconfigure_the_default_one()
    {
        using var host = buildHost(services =>
        {
            services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = DefaultSchema;
                    opts.Projections.Subscribe(new Bug5705Subscription());
                })
                .AddAsyncDaemon(DaemonMode.Solo);

            configureAncillary(services);

            services.AddHealthChecks()
                .AddMartenHighWaterHealthCheck(staleThreshold: 1.Hours())
                .AddMartenHighWaterHealthCheck<IBug5705Store>(staleThreshold: 200.Milliseconds());
        });

        await stallAsync(host.Services.GetRequiredService<IDocumentStore>(), mark: 1);
        await stallAsync(host.Services.GetRequiredService<IBug5705Store>(), mark: 1);

        var report = await probeTwiceAsync(host, 200.Milliseconds());

        report.Entries[nameof(HighWaterHealthCheckExtensions.HighWaterHealthCheck)].Status
            .ShouldBe(HealthStatus.Healthy);
        report.Entries[HighWaterHealthCheckExtensions.HighWaterHealthCheckNameFor<IBug5705Store>()].Status
            .ShouldBe(HealthStatus.Unhealthy);
    }

    private sealed class RecordingCoordinator: Marten.Events.Daemon.Coordination.IProjectionCoordinator<IBug5705Store>
    {
        public List<string> AskedFor { get; } = new();

        public ValueTask<IProjectionDaemon> DaemonForDatabase(string databaseIdentifier)
        {
            AskedFor.Add(databaseIdentifier);
            throw new InvalidOperationException("Recording only");
        }

        public IProjectionDaemon DaemonForMainDatabase() => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<IProjectionDaemon>> AllDaemonsAsync() => throw new NotSupportedException();

        public Task PauseAsync() => Task.CompletedTask;

        public Task ResumeAsync() => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
