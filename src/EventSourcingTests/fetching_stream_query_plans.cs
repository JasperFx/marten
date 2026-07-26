using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class fetching_stream_query_plans: OneOffConfigurationsContext
{
    [Fact]
    public async Task fetch_stream_state_by_query_plan()
    {
        var streamId = theSession.Events.StartStream<Quest>(new QuestStarted { Name = "Destroy the One Ring" },
            new MembersJoined(1, "Hobbiton", "Frodo", "Sam")).Id;
        await theSession.SaveChangesAsync();

        var state = await theSession.QueryByPlanAsync(new FetchStreamStatePlan(streamId));

        state.ShouldNotBeNull();
        state.Id.ShouldBe(streamId);
        state.Version.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_stream_state_by_query_plan_with_string_identity()
    {
        StoreOptions(opts => opts.Events.StreamIdentity = StreamIdentity.AsString);

        theSession.Events.Append("one-ring", new QuestStarted { Name = "Destroy the One Ring" },
            new MembersJoined(1, "Hobbiton", "Frodo", "Sam"));
        await theSession.SaveChangesAsync();

        var state = await theSession.QueryByPlanAsync(new FetchStreamStatePlan("one-ring"));

        state.ShouldNotBeNull();
        state.Key.ShouldBe("one-ring");
        state.Version.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_stream_state_by_query_plan_for_missing_stream_is_null()
    {
        var state = await theSession.QueryByPlanAsync(new FetchStreamStatePlan(Guid.NewGuid()));

        state.ShouldBeNull();
    }

    #region sample_using_fetch_stream_plan

    [Fact]
    public async Task fetch_stream_by_query_plan()
    {
        var streamId = theSession.Events.StartStream<Quest>(new QuestStarted { Name = "Destroy the One Ring" },
            new MembersJoined(1, "Hobbiton", "Frodo", "Sam"),
            new MembersJoined(2, "Bree", "Aragorn")).Id;
        await theSession.SaveChangesAsync();

        var events = await theSession.QueryByPlanAsync(new FetchStreamPlan(streamId));

        events.Count.ShouldBe(3);
        events[0].Data.ShouldBeOfType<QuestStarted>();
    }

    #endregion

    [Fact]
    public async Task fetch_stream_by_query_plan_with_string_identity()
    {
        StoreOptions(opts => opts.Events.StreamIdentity = StreamIdentity.AsString);

        theSession.Events.Append("one-ring", new QuestStarted { Name = "Destroy the One Ring" },
            new MembersJoined(1, "Hobbiton", "Frodo", "Sam"));
        await theSession.SaveChangesAsync();

        var events = await theSession.QueryByPlanAsync(new FetchStreamPlan("one-ring"));

        events.Count.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_stream_by_query_plan_with_version_cap()
    {
        var streamId = theSession.Events.StartStream<Quest>(new QuestStarted { Name = "Destroy the One Ring" },
            new MembersJoined(1, "Hobbiton", "Frodo", "Sam"),
            new MembersJoined(2, "Bree", "Aragorn")).Id;
        await theSession.SaveChangesAsync();

        var events = await theSession.QueryByPlanAsync(new FetchStreamPlan(streamId, version: 2));

        events.Count.ShouldBe(2);
        events[^1].Version.ShouldBe(2);
    }

    [Fact]
    public async Task fetch_stream_by_query_plan_for_missing_stream_is_empty()
    {
        var events = await theSession.QueryByPlanAsync(new FetchStreamPlan(Guid.NewGuid()));

        events.ShouldBeEmpty();
    }

    #region sample_fetch_stream_plans_in_batch

    [Fact]
    public async Task use_both_plans_in_one_batch()
    {
        var streamId = theSession.Events.StartStream<Quest>(new QuestStarted { Name = "Destroy the One Ring" },
            new MembersJoined(1, "Hobbiton", "Frodo", "Sam")).Id;
        await theSession.SaveChangesAsync();

        // Start a batch query
        var batch = theSession.CreateBatchQuery();

        // Fetching the stream state and the raw events of the same stream
        // in one database round trip
        var stateFetcher = batch.QueryByPlan(new FetchStreamStatePlan(streamId));
        var eventsFetcher = batch.QueryByPlan(new FetchStreamPlan(streamId));

        // Execute the batch query
        await batch.Execute();

        var state = await stateFetcher;
        var events = await eventsFetcher;

        state.ShouldNotBeNull();
        state.Version.ShouldBe(2);
        events.Count.ShouldBe(2);
    }

    #endregion
}
