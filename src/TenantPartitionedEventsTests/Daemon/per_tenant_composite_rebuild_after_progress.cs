#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// A per-tenant rebuild resets the tenant's progression with DeleteProjectionProgressAsync(..., tenantId). For a
/// composite that reset dropped only the composite's own row, not its members' per-tenant rows. Once the members had
/// progressed (any continuous run), the rebuild's first batch inserted every member's progression from the floor and
/// failed on pk_mt_event_progression, pausing the shard with nothing rebuilt.
/// </summary>
public partial class per_tenant_composite_rebuild_after_progress
{
    public class RebuiltTrip { public Guid Id { get; set; } public double Distance { get; set; } }
    public class RebuiltCount { public Guid Id { get; set; } public int Count { get; set; } }

    public record RebuiltStarted(Guid Id);
    public record RebuiltLeg(double Distance);

    public partial class RebuiltTripProjection: SingleStreamProjection<RebuiltTrip, Guid>
    {
        public RebuiltTripProjection() { Name = "RebuiltTrip"; }
        public void Apply(RebuiltTrip a, RebuiltLeg e) => a.Distance += e.Distance;
    }

    public partial class RebuiltCountProjection: SingleStreamProjection<RebuiltCount, Guid>
    {
        public RebuiltCountProjection() { Name = "RebuiltCount"; }
        public void Apply(RebuiltCount a, RebuiltLeg e) => a.Count++;
    }

    private const string Composite = "rebuilt-composite";
    private const int Streams = 10;
    private static readonly string[] Tenants = ["rb_a", "rb_b"];

    [Fact]
    public async Task rebuilding_one_tenant_after_its_members_have_progressed()
    {
        var token = TestContext.Current.CancellationToken;

        using var store = (DocumentStore)DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = $"rebuilt_composite_p{Environment.ProcessId}";
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Projections.CompositeProjectionFor(Composite, c =>
            {
                c.Add<RebuiltTripProjection>(stageNumber: 1);
                c.Add<RebuiltCountProjection>(stageNumber: 2);
            });
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync(token);
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), token);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, Tenants);

        foreach (var tenant in Tenants)
        {
            await using var session = store.LightweightSession(tenant);
            for (var i = 0; i < Streams; i++)
            {
                var id = Guid.NewGuid();
                session.Events.StartStream<RebuiltTrip>(id,
                    new RebuiltStarted(id), new RebuiltLeg(1), new RebuiltLeg(2), new RebuiltLeg(3));
            }

            await session.SaveChangesAsync(token);
        }

        // Continuous first, so every member has a per-tenant progression row
        using (var daemon = await store.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await waitForAsync(store, Tenants, token);
            await daemon.StopAllAsync();
        }

        using (var daemon = await store.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync(Composite, Tenants[0], 60.Seconds(), token);
        }

        foreach (var tenant in Tenants)
        {
            await using var query = store.QuerySession(tenant);

            var trips = await query.Query<RebuiltTrip>().ToListAsync(token);
            trips.Count.ShouldBe(Streams, $"tenant {tenant}");
            trips.ShouldAllBe(x => x.Distance == 6);

            var counts = await query.Query<RebuiltCount>().ToListAsync(token);
            counts.Count.ShouldBe(Streams, $"tenant {tenant}");
            counts.ShouldAllBe(x => x.Count == 3);
        }
    }

    private static async Task waitForAsync(DocumentStore store, string[] tenants, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < 30.Seconds())
        {
            var done = 0;
            foreach (var tenant in tenants)
            {
                await using var query = store.QuerySession(tenant);
                if (await query.Query<RebuiltCount>().CountAsync(x => x.Count == 3, token) == Streams) done++;
            }

            if (done == tenants.Length) return;
            await Task.Delay(250, token);
        }

        throw new TimeoutException("The continuous composite never caught up");
    }
}
