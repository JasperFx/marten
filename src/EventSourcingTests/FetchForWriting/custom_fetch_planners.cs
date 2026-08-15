using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using Marten.Testing.Harness;
using Marten.Testing.OtherAssembly.CustomFetchPlanners;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// Covers StoreOptions.Projections.FetchPlanners as a supported extension point: an application
/// can register its own IFetchPlanner to take over FetchForWriting() for the aggregate types it
/// recognises, and everything it declines keeps Marten's built-in behavior.
///
/// ExternalFetchPlanner deliberately lives in Marten.Testing.OtherAssembly, which is *not* granted
/// InternalsVisibleTo — see the note on that type.
/// </summary>
public class custom_fetch_planners: OneOffConfigurationsContext
{
    private ExternalFetchPlanner thePlanner = null!;

    public custom_fetch_planners()
    {
        // UseExternalFetchPlanner does the FetchPlanners.Add(...) from an assembly that cannot
        // see Marten's internals, so the registration itself is part of what this covers.
        StoreOptions(opts => thePlanner = opts.UseExternalFetchPlanner(typeof(SimpleAggregate)));
    }

    [Fact]
    public async Task custom_planner_wins_for_the_aggregate_type_it_matches()
    {
        // The built-in LiveFetchPlanner would happily resolve SimpleAggregate, so reaching the
        // external plan at all proves custom planners run ahead of the built-ins.
        await Should.ThrowAsync<ExternalFetchPlanWasUsedException>(async () =>
            await theSession.Events.FetchForWriting<SimpleAggregate>(Guid.NewGuid()));
    }

    [Fact]
    public async Task custom_planner_wins_for_fetch_latest_too()
    {
        await Should.ThrowAsync<ExternalFetchPlanWasUsedException>(async () =>
            await theSession.Events.FetchLatest<SimpleAggregate>(Guid.NewGuid()));
    }

    [Fact]
    public async Task declining_a_type_falls_through_to_the_built_in_planners()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate2>(streamId, new AEvent(), new BEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        // SimpleAggregate2 is not the type the planner matches, so Marten falls back to live
        // aggregation and nothing about the default behavior changes.
        var stream = await theSession.Events.FetchForWriting<SimpleAggregate2>(streamId);

        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.ACount.ShouldBe(1);
        stream.Aggregate.BCount.ShouldBe(2);
        stream.CurrentVersion.ShouldBe(3);
    }

    [Fact]
    public async Task the_planner_is_consulted_for_every_aggregate_type()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate2>(streamId, new AEvent());
        await theSession.SaveChangesAsync();

        await theSession.Events.FetchForWriting<SimpleAggregate2>(streamId);

        thePlanner.TryMatchCount.ShouldBeGreaterThan(0);
    }
}
