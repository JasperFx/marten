using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Marten.AspNetCore.Testing;

public record ParkedHealthWeighed(bool Poison);

public class ParkedHealthWeighIn
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public void Apply(ParkedHealthWeighed e)
    {
        if (e.Poison) throw new InvalidOperationException("Can't weigh this one");
        Count++;
    }
}

public interface IParkedHealthStore: IDocumentStore;

/// <summary>
///     marten#6122: the parked identities health check is Degraded while anything is parked, Healthy again once the
///     identity is discarded, and Unhealthy while a shard is paused at the parking cap. The hosts are built and never
///     started, so the only daemon running is the one each test builds.
/// </summary>
public class parked_identities_health_check
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static void configure(StoreOptions opts, string schema)
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = schema;
        opts.Projections.Errors.ApplyErrors = ProjectionErrorAction.Park;
        opts.Projections.Snapshot<ParkedHealthWeighIn>(SnapshotLifecycle.Async);
    }

    private static IHost buildHost(Action<IServiceCollection> configure)
        => Host.CreateDefaultBuilder().ConfigureServices(configure).Build();

    private static async Task<IDocumentStore> resetAsync(IDocumentStore store)
    {
        await store.Advanced.Clean.CompletelyRemoveAllAsync(Token);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        return store;
    }

    private static async Task weighAsync(IDocumentStore store, Guid stream, params bool[] poison)
    {
        await using var session = store.LightweightSession();
        session.Events.StartStream<ParkedHealthWeighIn>(stream, poison.Select(x => (object)new ParkedHealthWeighed(x)).ToArray());
        await session.SaveChangesAsync(Token);
    }

    private static async Task<HealthReportEntry> probeAsync(IHost host, string name)
    {
        var report = await host.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(x => x.Name == name, Token);
        return report.Entries[name];
    }

    [Fact]
    public async Task degraded_while_an_identity_is_parked_and_healthy_again_after_a_discard()
    {
        using var host = buildHost(services =>
        {
            services.AddMarten(opts => configure(opts, "parked_health"));

            #region sample_registering_the_parked_identities_health_check

            services.AddHealthChecks().AddMartenParkedIdentitiesHealthCheck();

            #endregion
        });

        var store = await resetAsync(host.Services.GetRequiredService<IDocumentStore>());

        var healthy = await probeAsync(host, "ParkedIdentitiesHealthCheck");
        healthy.Status.ShouldBe(HealthStatus.Healthy);
        healthy.Tags.ShouldContain("Parked");

        var stream = Guid.NewGuid();
        await weighAsync(store, stream, false, true);
        await weighAsync(store, Guid.NewGuid(), false);

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        try
        {
            await store.WaitForNonStaleProjectionDataAsync(30.Seconds());

            var degraded = await probeAsync(host, "ParkedIdentitiesHealthCheck");
            degraded.Status.ShouldBe(HealthStatus.Degraded);
            degraded.Description.ShouldNotBeNull().ShouldContain("1 projection identity is parked");
            degraded.Description.ShouldContain("ParkedHealthWeighIn");

            (await daemon.DiscardParkedIdentityAsync("ParkedHealthWeighIn", null, stream, Token)).Outcome
                .ShouldBe(ParkedReplayOutcome.Discarded);

            (await probeAsync(host, "ParkedIdentitiesHealthCheck")).Status.ShouldBe(HealthStatus.Healthy);
        }
        finally
        {
            await daemon.StopAllAsync();
        }
    }

    [Fact]
    public async Task unhealthy_while_a_shard_is_paused_at_the_parking_cap()
    {
        using var host = buildHost(services =>
        {
            services.AddMarten(opts =>
            {
                configure(opts, "parked_health_cap");

                // Two failing identities in one batch pass a cap of one
                opts.Projections.Errors.MaxParkedIdentitiesPerShard = 1;
            });

            services.AddHealthChecks().AddMartenParkedIdentitiesHealthCheck();
        });

        var store = await resetAsync(host.Services.GetRequiredService<IDocumentStore>());

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        try
        {
            // The shard's very first batch pauses, before it has committed anything (marten#6127)
            await using (var session = store.LightweightSession())
            {
                session.Events.StartStream<ParkedHealthWeighIn>(Guid.NewGuid(), new ParkedHealthWeighed(true));
                session.Events.StartStream<ParkedHealthWeighIn>(Guid.NewGuid(), new ParkedHealthWeighed(true));
                await session.SaveChangesAsync(Token);
            }

            // The pause is published asynchronously, so wait for the extended progression row to carry it
            var deadline = DateTimeOffset.UtcNow.Add(30.Seconds());
            while (!(await store.Storage.Database.AllProjectionProgress(Token))
                       .Any(x => x.Failure?.Category == ShardFailureCategory.ParkedLimitReached))
            {
                DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, "The shard never paused at the parking cap");
                await Task.Delay(100.Milliseconds(), Token);
            }

            var unhealthy = await probeAsync(host, "ParkedIdentitiesHealthCheck");
            unhealthy.Status.ShouldBe(HealthStatus.Unhealthy);
            unhealthy.Description.ShouldNotBeNull().ShouldContain("Paused at the parking cap: ParkedHealthWeighIn:All");
        }
        finally
        {
            await daemon.StopAllAsync();
        }
    }

    [Fact]
    public async Task a_store_registered_through_AddMartenStore_gets_its_own_check()
    {
        using var host = buildHost(services =>
        {
            services.AddMartenStore<IParkedHealthStore>(opts => configure(opts, "parked_health_ancillary"));
            services.AddHealthChecks().AddMartenParkedIdentitiesHealthCheck<IParkedHealthStore>();
        });

        var store = await resetAsync(host.Services.GetRequiredService<IParkedHealthStore>());

        var stream = Guid.NewGuid();
        await weighAsync(store, stream, true);

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        try
        {
            await store.WaitForNonStaleProjectionDataAsync(30.Seconds());

            var entry = await probeAsync(host, $"ParkedIdentitiesHealthCheck:{nameof(IParkedHealthStore)}");
            entry.Status.ShouldBe(HealthStatus.Degraded);
            entry.Tags.ShouldContain(nameof(IParkedHealthStore));
        }
        finally
        {
            await daemon.StopAllAsync();
        }
    }
}
