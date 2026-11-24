using System;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Xunit;
using static Marten.Events.Daemon.AsyncDaemonHealthCheckExtensions;

namespace Marten.AspNetCore.Testing;

public interface IBug6189Store: IDocumentStore;

public record Bug6189Started;

public class Bug6189Aggregate
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

public partial class Bug6189Projection: SingleStreamProjection<Bug6189Aggregate, Guid>
{
    public Bug6189Aggregate Create(Bug6189Started _) => new();
}

/// <summary>
///     marten#6189: with <c>maxSameLagTime</c>, the async daemon health check re-stamped every lagging shard on every
///     probe, so the "lagging since" time was always the previous probe and a check probed more often than the
///     threshold never turned unhealthy. Also: only <c>{Name}:All</c> was checked, so versioned and per-tenant
///     shards never were, and there was no overload for an ancillary store.
///     <para>
///         No daemon runs here: the progression rows are written by hand, so a lagging shard stays exactly where it
///         was put.
///     </para>
/// </summary>
public class async_daemon_health_check_lag_time
{
    private const string Schema = "bug_6189";
    private const string AncillarySchema = "bug_6189_ancillary";

    private static readonly TimeSpan MaxSameLagTime = TimeSpan.FromSeconds(30);

    private readonly SettableTimeProvider _time = new(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero));

    private static DocumentStore storeFor(string schema, uint version = 1)
    {
        return DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            var projection = new Bug6189Projection();
            if (version > 1) projection.Version = version;
            opts.Projections.Add(projection, ProjectionLifecycle.Async);
        });
    }

    private static async Task seedProgressAsync(IDocumentStore store, params (string Name, long Sequence)[] rows)
    {
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        foreach (var (name, sequence) in rows)
        {
            await using var cmd = new NpgsqlCommand(
                $"insert into {store.Options.Events.DatabaseSchemaName}.mt_event_progression (name, last_seq_id, last_updated) " +
                "values ($1, $2, now()) on conflict (name) do update set last_seq_id = excluded.last_seq_id", conn);
            cmd.Parameters.Add(new NpgsqlParameter { Value = name });
            cmd.Parameters.Add(new NpgsqlParameter { Value = sequence });
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private AsyncDaemonHealthCheck checkFor(IDocumentStore store)
    {
        return new AsyncDaemonHealthCheck(store, new AsyncDaemonHealthCheckSettings(10, MaxSameLagTime), _time,
            new ProjectionStateTracker());
    }

    [Fact]
    public async Task turns_unhealthy_when_probed_more_often_than_the_threshold()
    {
        await using var store = storeFor(Schema);
        await seedProgressAsync(store, ("HighWaterMark", 100), ("Bug6189Aggregate:All", 0));

        var check = checkFor(store);

        // Probed every 10 seconds, a shard stuck at the same sequence for 30 seconds is unhealthy
        for (var i = 0; i < 3; i++)
        {
            (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);
            _time.Advance(TimeSpan.FromSeconds(10));
        }

        var result = await check.CheckHealthAsync(new HealthCheckContext());
        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldContain("Bug6189Aggregate:All");
    }

    [Fact]
    public async Task a_shard_that_moves_starts_its_clock_again()
    {
        await using var store = storeFor(Schema);
        await seedProgressAsync(store, ("HighWaterMark", 100), ("Bug6189Aggregate:All", 0));

        var check = checkFor(store);
        await check.CheckHealthAsync(new HealthCheckContext());

        _time.Advance(TimeSpan.FromSeconds(20));
        await seedSequenceAsync(store, "Bug6189Aggregate:All", 5);
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);

        _time.Advance(TimeSpan.FromSeconds(20));
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Healthy);

        _time.Advance(TimeSpan.FromSeconds(11));
        (await check.CheckHealthAsync(new HealthCheckContext())).Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task checks_a_versioned_shard()
    {
        await using var store = storeFor(Schema, version: 2);
        await seedProgressAsync(store, ("HighWaterMark", 100), ("Bug6189Aggregate:V2:All", 0));

        var check = new AsyncDaemonHealthCheck(store, new AsyncDaemonHealthCheckSettings(10), _time,
            new ProjectionStateTracker());

        var result = await check.CheckHealthAsync(new HealthCheckContext());
        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldContain("Bug6189Aggregate:V2:All");
    }

    [Fact]
    public async Task checks_a_per_tenant_shard()
    {
        await using var store = storeFor(Schema);
        await seedProgressAsync(store, ("HighWaterMark", 100), ("Bug6189Aggregate:All", 100),
            ("Bug6189Aggregate:All:tenant_a", 0));

        var check = new AsyncDaemonHealthCheck(store, new AsyncDaemonHealthCheckSettings(10), _time,
            new ProjectionStateTracker());

        var result = await check.CheckHealthAsync(new HealthCheckContext());
        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldContain("Bug6189Aggregate:All:tenant_a");
    }

    [Fact]
    public async Task probes_an_ancillary_store()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMartenStore<IBug6189Store>(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = AncillarySchema;
                        opts.Projections.Add(new Bug6189Projection(), ProjectionLifecycle.Async);
                    })
                    .AddAsyncDaemon(DaemonMode.Solo);

                services.AddHealthChecks().AddMartenAsyncDaemonHealthCheck<IBug6189Store>(maxEventLag: 10);
            })
            .Build();

        var store = host.Services.GetRequiredService<IBug6189Store>();
        await seedProgressAsync(store, ("HighWaterMark", 100), ("Bug6189Aggregate:All", 0));

        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        var entry = report.Entries[AsyncDaemonHealthCheckNameFor<IBug6189Store>()];
        entry.Exception.ShouldBeNull();
        entry.Status.ShouldBe(HealthStatus.Unhealthy);
        entry.Tags.ShouldContain(nameof(IBug6189Store));
    }

    private static async Task seedSequenceAsync(IDocumentStore store, string name, long sequence)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"update {store.Options.Events.DatabaseSchemaName}.mt_event_progression set last_seq_id = $2 where name = $1", conn);
        cmd.Parameters.Add(new NpgsqlParameter { Value = name });
        cmd.Parameters.Add(new NpgsqlParameter { Value = sequence });
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class SettableTimeProvider(DateTimeOffset now): TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
