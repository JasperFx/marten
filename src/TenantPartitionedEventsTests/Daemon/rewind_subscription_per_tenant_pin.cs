using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// #4617 section 4 pinned <c>IProjectionDaemon.RewindSubscriptionAsync(name, tenantId, …)</c>
/// throwing <see cref="NotSupportedException"/>, so that a per-tenant rewind landing would flip the
/// assertion deliberately. jasperfx#1025 (JasperFx 2.91.0) implemented it in the shared daemon: only
/// that tenant's agents stop, only its progression rows are rewound, and its agents restart against
/// its own high-water mark. These facts now pin that behaviour. The tenant-less overload still works.
/// </summary>
[Collection("guid-partitioned")]
public class rewind_subscription_per_tenant_pin
{
    private static readonly TimeSpan ReplayTimeout = TimeSpan.FromSeconds(60);

    private readonly GuidPartitionedFixture _fixture;

    public rewind_subscription_per_tenant_pin(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task per_tenant_RewindSubscriptionAsync_replays_only_that_tenant()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var alphaStreamId = Guid.NewGuid();
        var betaStreamId = Guid.NewGuid();

        await using (var session = _fixture.Store.LightweightSession(alpha))
        {
            session.Events.StartStream<TripDistance>(alphaStreamId,
                new TripStarted(alphaStreamId), new TripLeg(10), new TripLeg(20));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var session = _fixture.Store.LightweightSession(beta))
        {
            session.Events.StartStream<TripDistance>(betaStreamId,
                new TripStarted(betaStreamId), new TripLeg(7), new TripLeg(13));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var daemon = await _fixture.Store.BuildProjectionDaemonAsync();
        try
        {
            // Both tenants' per-tenant progression rows reach their own heads
            await daemon.RebuildProjectionAsync(TripDistanceProjection.ProjectionName, alpha, CancellationToken.None);
            await daemon.RebuildProjectionAsync(TripDistanceProjection.ProjectionName, beta, CancellationToken.None);

            var betaProgression = ShardName.Compose(TripDistanceProjection.ProjectionName).ForTenant(beta).Identity;
            var betaProgressionBefore = await ReadProgressionAsync(_fixture.SchemaName, betaProgression);
            betaProgressionBefore.ShouldBeGreaterThan(0);

            // Remove both projected documents out of band. Only a replay can bring one back, so a
            // document reappearing is the evidence that its tenant was rewound
            await hardDeleteAsync(alpha, alphaStreamId);
            await hardDeleteAsync(beta, betaStreamId);

            await daemon.RewindSubscriptionAsync(TripDistanceProjection.ProjectionName, alpha,
                CancellationToken.None, sequenceFloor: 0);

            var replayed = await waitForDocumentAsync(alpha, alphaStreamId);
            replayed.Distance.ShouldBe(30, "alpha's events are replayed from the start: 10 + 20");

            await using (var session = _fixture.Store.QuerySession(beta))
            {
                (await session.LoadAsync<TripDistance>(betaStreamId, TestContext.Current.CancellationToken))
                    .ShouldBeNull("beta was not rewound, so nothing replays its events");
            }

            (await ReadProgressionAsync(_fixture.SchemaName, betaProgression))
                .ShouldBe(betaProgressionBefore, "rewinding alpha must leave beta's progression row alone");

            // While this daemon runs a tenant agent of the projection, the tenant-less rewind refuses a
            // sequence number, which identifies a position in one tenant's events only (jasperfx#1025)
            await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
                await daemon.RewindSubscriptionAsync(TripDistanceProjection.ProjectionName, tenantId: null,
                    CancellationToken.None, sequenceFloor: 1));
        }
        finally
        {
            await daemon.StopAllAsync();
        }
    }

    private async Task hardDeleteAsync(string tenantId, Guid id)
    {
        await using var session = _fixture.Store.LightweightSession(tenantId);
        session.HardDelete<TripDistance>(id);
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<TripDistance> waitForDocumentAsync(string tenantId, Guid id)
    {
        var deadline = DateTimeOffset.UtcNow.Add(ReplayTimeout);
        while (true)
        {
            await using (var session = _fixture.Store.QuerySession(tenantId))
            {
                var document = await session.LoadAsync<TripDistance>(id, TestContext.Current.CancellationToken);
                if (document != null) return document;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Tenant {tenantId}'s TripDistance {id} was not replayed within {ReplayTimeout}");
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private static async Task<long> ReadProgressionAsync(string schema, string name)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select last_seq_id from {schema}.mt_event_progression where name = @n";
        cmd.Parameters.AddWithValue("n", name);
        var v = await cmd.ExecuteScalarAsync();
        return v == null || v == DBNull.Value ? 0L : Convert.ToInt64(v);
    }

    [Fact]
    public async Task tenantless_RewindSubscriptionAsync_does_NOT_throw_under_partitioning()
    {
        // Null tenant id falls through to the store-global RewindSubscriptionAsync
        // path, which Marten DOES implement. Pin that this still works under
        // partitioning — only the per-tenant overload is gated.
        using var daemon = await _fixture.Store.BuildProjectionDaemonAsync();

        await Should.NotThrowAsync(async () =>
            await daemon.RewindSubscriptionAsync(
                TripDistanceProjection.ProjectionName,
                tenantId: null,
                CancellationToken.None,
                sequenceFloor: 0));
    }
}
