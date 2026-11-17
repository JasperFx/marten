#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Postgresql;
using Xunit;
using IProjectionCoordinator = Marten.Events.Daemon.Coordination.IProjectionCoordinator;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// Under <c>UseTenantPartitionedEvents</c> the daemon finds the tenants to run per-tenant agents for in the
/// <c>mt_tenant_partitions</c> lookup table (<c>ICrossTenantRebuildSource.FindRebuildTenantsAsync</c>), and a
/// projection with no registered tenant keeps its store-global shard. The lookup table is created by a full
/// schema apply or by registering the first tenant, not by the event storage, so on a store whose schema is
/// created lazily and that has no tenant yet the read failed with 42P01 -- and with it <c>StartAllAsync</c>,
/// the store-global rebuild and <c>CatchUpAsync</c>.
/// </summary>
public class daemon_before_the_first_tenant_is_registered: PartitionedStoreContext
{
    protected override string SchemaPrefix => "tp_dbt";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Projections.Add<TripDistanceProjection>(ProjectionLifecycle.Async);
        opts.Schema.For<TripDistance>().Identity(x => x.Id).DocumentAlias("dbt_trip_dist");
    }

    [Fact]
    public async Task there_are_no_tenants_to_run_agents_for()
    {
        // The shape this class is about: event storage exists, the tenant lookup table does not.
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync(TestContext.Current.CancellationToken);
            await using var cmd = conn.CreateCommand(
                $"select to_regclass('{Schema}.mt_events') is not null, to_regclass('{Schema}.mt_tenant_partitions') is not null");
            await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            await reader.ReadAsync(TestContext.Current.CancellationToken);
            reader.GetBoolean(0).ShouldBeTrue("the event storage was created");
            reader.GetBoolean(1).ShouldBeFalse("no tenant was registered and no full schema apply ran");
        }

        var tenants = await ((ICrossTenantRebuildSource)Store.Storage.Database)
            .FindRebuildTenantsAsync(TripDistanceProjection.ProjectionName, TestContext.Current.CancellationToken);

        tenants.ShouldBeEmpty();
    }

    [Fact]
    public async Task start_all_runs_the_store_global_shard()
    {
        using var daemon = await Store.BuildProjectionDaemonAsync();

        await daemon.StartAllAsync();

        daemon.CurrentAgents()
            .Where(x => x.Status == AgentStatus.Running)
            .Select(x => x.Name.Identity)
            .ShouldBe([$"{TripDistanceProjection.ProjectionName}:All"]);
    }

    [Fact]
    public async Task a_store_global_rebuild_has_nothing_to_rebuild()
    {
        using var daemon = await Store.BuildProjectionDaemonAsync();

        await daemon.RebuildProjectionAsync(TripDistanceProjection.ProjectionName, null,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task catch_up_has_nothing_to_catch_up()
    {
        using var daemon = await Store.BuildProjectionDaemonAsync();

        await daemon.CatchUpAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>
/// The hosted daemon's coordinator expands each store-global shard into per-tenant shards on every leadership
/// cycle (jasperfx#491), keeps the store-global shard while no tenant is registered, and reaps it once the first
/// tenant's agent replaces it. Before the first tenant on a store whose schema is created lazily, every cycle
/// failed with 42P01 ("Error trying to resolve projection distributions") and no agent started. Run against a
/// store whose schema was applied in full as well, where the lookup table exists but is empty: both must behave
/// the same way.
/// </summary>
public class coordinator_before_the_first_tenant_is_registered
{
    private static readonly string AppliedSchema = $"tp_cft_applied_p{Environment.ProcessId}";
    private static readonly string LazySchema = $"tp_cft_lazy_p{Environment.ProcessId}";

    private static Action<StoreOptions> Configure(string schema) => o =>
    {
        o.Connection(ConnectionSource.ConnectionString);
        o.DatabaseSchemaName = schema;
        o.DisableNpgsqlLogging = true;
        o.Events.TenancyStyle = TenancyStyle.Conjoined;
        o.Events.UseTenantPartitionedEvents = true;
        o.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
        o.Policies.AllDocumentsAreMultiTenanted();

        // The hand-over converges on the coordinator's leadership polling cycle.
        o.Projections.LeadershipPollingTime = 250;

        o.Schema.For<TripDistance>().Identity(x => x.Id).DocumentAlias("cft_trip_dist");
        o.Projections.Add<TripDistanceProjection>(ProjectionLifecycle.Async);
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task runs_the_store_global_shard_until_the_first_tenant_takes_over(bool applySchemaFirst)
    {
        var schema = applySchemaFirst ? AppliedSchema : LazySchema;
        using var store = (DocumentStore)DocumentStore.For(Configure(schema));

        await store.Advanced.Clean.CompletelyRemoveAllAsync(TestContext.Current.CancellationToken);
        if (applySchemaFirst)
        {
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        }
        else
        {
            await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), TestContext.Current.CancellationToken);
        }

        using var node = await new HostBuilder()
            .ConfigureServices(services => services.AddMarten(Configure(schema)).AddAsyncDaemon(DaemonMode.Solo))
            .StartAsync(TestContext.Current.CancellationToken);
        var daemon = node.Services.GetRequiredService<IProjectionCoordinator>().DaemonForMainDatabase();

        try
        {
            var storeGlobal = $"{TripDistanceProjection.ProjectionName}:All";
            await WaitForAsync(() => RunningAgents(daemon).SequenceEqual([storeGlobal]), 15.Seconds(),
                "the coordinator runs the store-global shard while no tenant is registered");

            const string tenant = "cft_first";
            await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

            var streamId = Guid.NewGuid();
            await using (var session = store.LightweightSession(tenant))
            {
                session.Events.StartStream<TripDistance>(streamId,
                    new TripStarted(streamId), new TripLeg(1), new TripLeg(2), new TripLeg(4));
                await session.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            var perTenant = $"{storeGlobal}:{tenant}";
            await WaitForAsync(() => RunningAgents(daemon).SequenceEqual([perTenant]), 15.Seconds(),
                "the first tenant's agent replaces the store-global one");

            TripDistance? doc = null;
            await WaitForAsync(async () =>
                {
                    await using var query = store.QuerySession(tenant);
                    doc = await query.LoadAsync<TripDistance>(streamId, TestContext.Current.CancellationToken);
                    return doc is { Version: 4 };
                }, 15.Seconds(), "the first tenant's projection catches up");

            // Every leg was applied exactly once, by one agent.
            doc!.Distance.ShouldBe(7);
        }
        finally
        {
            await node.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private static IReadOnlyList<string> RunningAgents(IProjectionDaemon daemon) =>
        daemon.CurrentAgents()
            .Where(x => x.Status == AgentStatus.Running)
            .Select(x => x.Name.Identity)
            .ToList();

    private static Task WaitForAsync(Func<bool> condition, TimeSpan timeout, string what) =>
        WaitForAsync(() => Task.FromResult(condition()), timeout, what);

    private static async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout, string what)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Timed out after {timeout} waiting for: {what}");
    }
}
