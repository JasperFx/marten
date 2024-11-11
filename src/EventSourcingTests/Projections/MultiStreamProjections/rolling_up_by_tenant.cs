using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections.MultiStreamProjections;

public class rolling_up_by_tenant : OneOffConfigurationsContext
{
    [Fact]
    public async Task track_totals_by_tenant_id()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.Add<RollupProjection>(ProjectionLifecycle.Async);
            opts.Events.EnableGlobalProjectionsForConjoinedTenancy = true;
        });

        var session1 = theStore.LightweightSession("one");
        session1.Events.StartStream(new AEvent(), new BEvent());
        session1.Events.StartStream(new BEvent(), new BEvent());
        session1.Events.StartStream(new BEvent(), new BEvent());
        await session1.SaveChangesAsync();

        var session2 = theStore.LightweightSession("two");
        session2.Events.StartStream(new AEvent(), new AEvent());
        session2.Events.StartStream(new BEvent(), new AEvent());
        session2.Events.StartStream(new BEvent(), new BEvent());
        await session2.SaveChangesAsync();

        var session3 = theStore.LightweightSession("three");
        session3.Events.StartStream(new AEvent(), new AEvent());
        session3.Events.StartStream(new AEvent(), new AEvent());
        session3.Events.StartStream(new BEvent(), new BEvent());
        await session3.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        await daemon.WaitForNonStaleData(30.Seconds());

        (await theSession.LoadAsync<Rollup>("one")).ACount.ShouldBe(1);
        (await theSession.LoadAsync<Rollup>("two")).ACount.ShouldBe(3);
        (await theSession.LoadAsync<Rollup>("three")).ACount.ShouldBe(4);
    }
}

#region sample_rollup_projection_by_tenant_id

public class RollupProjection: MultiStreamProjection<Rollup, string>
{
    public RollupProjection()
    {
        // This opts into doing the event slicing by tenant id
        RollUpByTenant();
    }

    public void Apply(Rollup state, AEvent e) => state.ACount++;
    public void Apply(Rollup state, BEvent e) => state.BCount++;
}

public class Rollup
{
    [Identity]
    public string TenantId { get; set; }
    public int ACount { get; set; }
    public int BCount { get; set; }
}

#endregion
