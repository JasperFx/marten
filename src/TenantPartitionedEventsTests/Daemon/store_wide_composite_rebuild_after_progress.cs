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
/// A store-wide rebuild of a composite under UseTenantPartitionedEvents, guarding two defects that each lost the
/// rebuild. Before jasperfx#1128 (JasperFx 2.100.0) the store-wide overloads ran one store-global replay; every tenant
/// numbers its events from 1, so a page's last seq_id skipped the other tenants' events at or below it that didn't
/// fit in the page. jasperfx#1128 fans the rebuild out per tenant instead, which runs the tenant-scoped reset, and
/// before marten#5829 that reset left the members' per-tenant progression rows behind, so a composite whose members
/// had progressed failed its first batch on pk_mt_event_progression.
/// </summary>
public partial class store_wide_composite_rebuild_after_progress
{
    public class WideTrip { public Guid Id { get; set; } public double Distance { get; set; } }
    public class WideCount { public Guid Id { get; set; } public int Count { get; set; } }

    public record WideStarted(Guid Id);
    public record WideLeg(double Distance);

    public partial class WideTripProjection: SingleStreamProjection<WideTrip, Guid>
    {
        public WideTripProjection() { Name = "WideTrip"; }
        public void Apply(WideTrip a, WideLeg e) => a.Distance += e.Distance;
    }

    public partial class WideCountProjection: SingleStreamProjection<WideCount, Guid>
    {
        public WideCountProjection() { Name = "WideCount"; }
        public void Apply(WideCount a, WideLeg e) => a.Count++;
    }

    private const string Composite = "wide-composite";
    private const int Streams = 20;

    // Six tenants and a page of 10 is the shape jasperfx#1128 reproduced the event loss with; one tenant does not lose
    // events, because only several tenants' events share seq_ids.
    private static readonly string[] Tenants = ["wr_a", "wr_b", "wr_c", "wr_d", "wr_e", "wr_f"];

    [Fact]
    public async Task rebuilding_every_tenant_after_the_members_have_progressed()
    {
        var token = TestContext.Current.CancellationToken;

        using var store = (DocumentStore)DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = $"wide_composite_p{Environment.ProcessId}";
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Projections.CompositeProjectionFor(Composite, c =>
            {
                c.Options.BatchSize = 10;
                c.Add<WideTripProjection>(stageNumber: 1);
                c.Add<WideCountProjection>(stageNumber: 2);
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
                session.Events.StartStream<WideTrip>(id,
                    new WideStarted(id), new WideLeg(1), new WideLeg(2), new WideLeg(3));
            }

            await session.SaveChangesAsync(token);
        }

        // Continuous first, so every member has a per-tenant progression row for the rebuild's reset to clear
        using (var daemon = await store.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await waitForAsync(store, token);
            await daemon.StopAllAsync();
        }

        // The store-wide overload, not the per-tenant one
        using (var daemon = await store.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync(Composite, 120.Seconds(), token);
        }

        foreach (var tenant in Tenants)
        {
            await using var query = store.QuerySession(tenant);

            var trips = await query.Query<WideTrip>().ToListAsync(token);
            trips.Count.ShouldBe(Streams, $"tenant {tenant}");
            trips.Where(x => x.Distance != 6).ShouldBeEmpty($"tenant {tenant} has trips missing legs");

            var counts = await query.Query<WideCount>().ToListAsync(token);
            counts.Count.ShouldBe(Streams, $"tenant {tenant}");
            counts.Where(x => x.Count != 3).ShouldBeEmpty($"tenant {tenant} has counts missing legs");
        }
    }

    private static async Task waitForAsync(DocumentStore store, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < 60.Seconds())
        {
            var done = 0;
            foreach (var tenant in Tenants)
            {
                await using var query = store.QuerySession(tenant);
                if (await query.Query<WideCount>().CountAsync(x => x.Count == 3, token) == Streams) done++;
            }

            if (done == Tenants.Length) return;
            await Task.Delay(250, token);
        }

        throw new TimeoutException("The continuous composite never caught up");
    }
}
