using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.Bugs;

/// <summary>
/// #5167 — <c>EnableExtendedProgressionTracking</c> used to cost one pooled connection and one
/// transaction per shard database every 5 seconds, on a hardcoded interval no application could reach.
/// At 512 tenant databases that is ~37 connection acquisitions per second per node purely for
/// telemetry, and it contends for the very rows the progress writer updates.
///
/// <para>
/// jasperfx#622 (JasperFx 2.39.0) turned the periodic beat OFF by default and put it behind
/// <c>DaemonSettings.ExtendedProgressionHeartbeatInterval</c>, which Marten exposes on
/// <c>opts.Projections</c>. These tests pin both halves of that from the Marten side, because the
/// difference is invisible from the API surface and easy to regress: with the default, only status
/// TRANSITIONS reach the database; with an interval configured, ordinary progress publications do too.
/// </para>
/// </summary>
public class Bug_5167_extended_progression_heartbeat_interval: OneOffConfigurationsContext
{
    [Fact]
    public void periodic_heartbeat_writes_are_off_by_default()
    {
        StoreOptions(x => x.Events.EnableExtendedProgressionTracking = true);

        theStore.Options.Projections.ExtendedProgressionHeartbeatInterval.ShouldBeNull();
    }

    [Fact]
    public async Task by_default_ordinary_progress_does_not_rewrite_the_heartbeat()
    {
        var heartbeats = await captureHeartbeatsAsync(interval: null);

        // The second batch's Updated publications carry a heartbeat, but with the periodic beat off they
        // are dropped rather than flushed -- no connection, no transaction, no row touched.
        heartbeats.After.ShouldBe(heartbeats.Before);
    }

    [Fact]
    public async Task configuring_an_interval_restores_the_periodic_heartbeat()
    {
        var heartbeats = await captureHeartbeatsAsync(interval: 1.Milliseconds());

        heartbeats.After.ShouldNotBeNull();
        heartbeats.After.ShouldNotBe(heartbeats.Before);
    }

    private async Task<(DateTime? Before, DateTime? After)> captureHeartbeatsAsync(TimeSpan? interval)
    {
        StoreOptions(x =>
        {
            x.Events.EnableExtendedProgressionTracking = true;
            x.Projections.ExtendedProgressionHeartbeatInterval = interval;
            x.Projections.Add(new Heartbeat5167Projection(), ProjectionLifecycle.Async);
        });

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        await appendAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(30.Seconds());

        var before = await readHeartbeatAsync();

        // A second batch produces ShardAction.Updated publications -- progress, not a status transition.
        await appendAsync();
        await daemon.WaitForNonStaleData(30.Seconds());

        // The write is posted to a background block, so give it a beat to land before reading.
        await Task.Delay(500, TestContext.Current.CancellationToken);
        var after = await readHeartbeatAsync();

        await daemon.StopAllAsync();

        return (before, after);
    }

    private async Task appendAsync()
    {
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<Heartbeat5167Doc>(Guid.NewGuid(), new Heartbeat5167Event());
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<DateTime?> readHeartbeatAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        var raw = await conn
            .CreateCommand(
                $"select heartbeat from {theStore.Events.DatabaseSchemaName}.mt_event_progression where name like 'Heartbeat5167%'")
            .ExecuteScalarAsync(TestContext.Current.CancellationToken);

        return raw is null or DBNull ? null : (DateTime)raw;
    }
}

public record Heartbeat5167Event;

public class Heartbeat5167Doc
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

public partial class Heartbeat5167Projection: SingleStreamProjection<Heartbeat5167Doc, Guid>
{
    public Heartbeat5167Projection()
    {
        Name = "Heartbeat5167";
    }

    public void Apply(Heartbeat5167Doc doc, Heartbeat5167Event _) => doc.Count++;
}
