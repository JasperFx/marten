#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// wolverine#3280 / JasperFx per-tenant agent start: under Wolverine-managed event-subscription
/// distribution the daemon is driven by <c>StartAgentAsync("&lt;proj&gt;:V{n}:All:&lt;tenant&gt;")</c> for
/// each per-tenant shard individually — NOT by <c>StartAllAsync</c> (which primes every tenant's ceiling
/// up front). A per-tenant agent therefore starts with its ceiling unknown (high-water 0), and the
/// per-tenant high-water poll only re-runs on a store-global high-water CHANGE tick. When a node starts
/// after the store-global mark is already stable — the common case for a node that joins after catch-up —
/// no further tick fires and the freshly-started agent would sit idle at 0 forever.
///
/// <para>
/// The fix (JasperFxAsyncDaemon.StartAgentAsync) drives one per-tenant poll immediately after a
/// per-tenant agent starts, so it is routed its own tenant's mark and advances. This test reproduces the
/// "start individually, no following global tick" shape: append events for two tenants, then start each
/// tenant's async agent one-by-one via the string overload (exactly what Wolverine calls) and assert both
/// tenants' projections catch up. It composes with the per-tenant high-water reading from max(seq_id)
/// (#4712-class fix) so the mark is correct even though the per-tenant sequence is never advanced.
/// </para>
/// </summary>
public partial class per_tenant_agent_start_routes_high_water
{
    private static readonly string SchemaName = $"pt_agent_start_p{Environment.ProcessId}";

    public class TenantTripDistance
    {
        public Guid Id { get; set; }
        public double Distance { get; set; }
        public int Version { get; set; }
    }

    public record TripStarted(Guid Id);
    public record TripLeg(double Distance);

    public partial class TenantTripDistanceProjection: SingleStreamProjection<TenantTripDistance, Guid>
    {
        public TenantTripDistanceProjection() => Name = "PtAgentStartTrip";
        public void Apply(TenantTripDistance agg, TripLeg @event) => agg.Distance += @event.Distance;
    }

    [Fact]
    public async Task starting_each_per_tenant_agent_individually_advances_its_projection()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.Add<TenantTripDistanceProjection>(ProjectionLifecycle.Async);
            // Nested type name + tenant suffix overflows PG's 64-byte identifier limit; shorten it.
            opts.Schema.For<TenantTripDistance>().Identity(x => x.Id).DocumentAlias("pt_agent_trip");
        });
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        var tenants = new[] { $"t_{Guid.NewGuid():N}"[..12], $"t_{Guid.NewGuid():N}"[..12] };
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenants);

        // A distinct stream per tenant, each with one TripLeg (Distance 1.0).
        var streamByTenant = new Dictionary<string, Guid>();
        foreach (var tenant in tenants)
        {
            var streamId = Guid.NewGuid();
            streamByTenant[tenant] = streamId;
            await using var session = store.LightweightSession(tenant);
            session.Events.StartStream<TenantTripDistance>(streamId, new TripStarted(streamId), new TripLeg(1.0));
            await session.SaveChangesAsync();
        }

        using var daemon = await store.BuildProjectionDaemonAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Start the FIRST tenant's agent and let it fully catch up. Starting it also starts high-water
        // detection, which advances the store-global mark to its final value and broadcasts it.
        var first = tenants[0];
        await daemon.StartAgentAsync(ShardName.Compose("PtAgentStartTrip", ShardName.All, first).Identity, CancellationToken.None);
        await WaitForProjectionAsync(store, first, streamByTenant[first], cts.Token);

        // Now start the SECOND tenant's agent. The store-global high-water is already at its final value, so
        // HighWaterAgent will NOT broadcast again (it publishes only on change — HighWaterAgent.cs:209), and
        // no store-global tick will ever fire the per-tenant poll for this agent. It therefore advances ONLY
        // if StartAgentAsync itself drives a per-tenant poll after starting it (the fix). Under the pre-fix
        // behavior it sits at high-water 0 forever and this wait times out — the deterministic regression.
        var second = tenants[1];
        await daemon.StartAgentAsync(ShardName.Compose("PtAgentStartTrip", ShardName.All, second).Identity, CancellationToken.None);
        await WaitForProjectionAsync(store, second, streamByTenant[second], cts.Token);
    }

    private static async Task WaitForProjectionAsync(
        IDocumentStore store, string tenant, Guid streamId, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await using var query = store.QuerySession(tenant);
            var doc = await query.LoadAsync<TenantTripDistance>(streamId, token);
            if (doc is { Distance: >= 1.0 })
            {
                return;
            }

            await Task.Delay(150, token);
        }

        throw new Xunit.Sdk.XunitException(
            $"per-tenant agent for {tenant} never advanced its projection (stream {streamId}); " +
            "its own high-water mark was not routed after StartAgentAsync.");
    }
}
