using System;
using System.Threading.Tasks;
using EventSourcingTests.Projections;
using Marten;
using Marten.Events;
using Marten.Schema.Identity;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class fetching_stream_state_before_aggregator_is_registered: IntegrationContext
{
    [Fact]
    public async Task bug_705_order_of_operation()
    {
        var streamId = CombGuidIdGeneration.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.StartStream<QuestParty>(streamId, joined, departed);
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.LightweightSession())
        {
            var state = await query.Events.FetchStreamStateAsync(streamId);
            var aggregate = await query.Events.AggregateStreamAsync<QuestParty>(streamId);

            state.ShouldNotBeNull();
            aggregate.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task other_try()
    {
        var store = DocumentStore.For(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);
            _.Events.AddEventTypes(new[] { typeof(FooEvent), });
        });

        using (var session = store.LightweightSession())
        {
            var aid = Guid.Parse("1442cbbb-a49a-497e-9ee8-715ed2833bf8");
            session.Events.StartStream<FooAggregate>(aid, new FooEvent());
            await session.SaveChangesAsync();
        }

        var store2 = DocumentStore.For(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);
            _.Events.AddEventTypes(new[] { typeof(FooEvent), });

            _.Projections.AggregatorFor<FooAggregate>();
        });

        using (var session = store2.LightweightSession())
        {
            var aid = Guid.Parse("1442cbbb-a49a-497e-9ee8-715ed2833bf8");
            var state = await session.Events.FetchStreamStateAsync(aid);
            // We never get to the AggregateStream call because we get a nullreference exception on the FetchStreamState call
            var aggregate = await session.Events.AggregateStreamAsync<FooAggregate>(aid);
        }
    }

    public fetching_stream_state_before_aggregator_is_registered(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}

public class FooEvent { }

public class FooAggregate
{
    public Guid Id;

    public void Apply(FooEvent e){}
}

#region sample_fetching_stream_state
public class fetching_stream_state: IntegrationContext
{
    private Guid theStreamId;

    public fetching_stream_state(DefaultStoreFixture fixture) : base(fixture)
    {

    }

    protected override Task fixtureSetup()
    {
        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        theStreamId = theSession.Events.StartStream<Quest>(joined, departed).Id;
        return theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task can_fetch_the_stream_version_and_aggregate_type()
    {
        var state = await theSession.Events.FetchStreamStateAsync(theStreamId);

        state.ShouldNotBeNull();
        state.Id.ShouldBe(theStreamId);
        state.Version.ShouldBe(2);
        state.AggregateType.ShouldBe(typeof(Quest));
        state.LastTimestamp.ShouldNotBe(DateTimeOffset.MinValue);
        state.Created.ShouldNotBe(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task can_fetch_the_stream_version_and_aggregate_type_async()
    {
        var state = await theSession.Events.FetchStreamStateAsync(theStreamId);

        state.ShouldNotBeNull();
        state.Id.ShouldBe(theStreamId);
        state.Version.ShouldBe(2);
        state.AggregateType.ShouldBe(typeof(Quest));
        state.LastTimestamp.ShouldNotBe(DateTimeOffset.MinValue);
        state.Created.ShouldNotBe(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task can_fetch_the_stream_version_through_batch_query()
    {
        var batch = theSession.CreateBatchQuery();

        var stateTask = batch.Events.FetchStreamState(theStreamId);

        await batch.Execute();

        var state = await stateTask;

        state.Id.ShouldBe(theStreamId);
        state.Version.ShouldBe(2);
        state.AggregateType.ShouldBe(typeof(Quest));
        state.LastTimestamp.ShouldNotBe(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task can_fetch_the_stream_events_through_batch_query()
    {
        var batch = theSession.CreateBatchQuery();

        var eventsTask = batch.Events.FetchStream(theStreamId);

        await batch.Execute();

        var events = await eventsTask;

        events.Count.ShouldBe(2);
    }
}

#endregion

public class fetching_stream_state_string_id: IntegrationContext
{
    private string theStreamKey;

    public fetching_stream_state_string_id(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    protected override Task fixtureSetup()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        theStreamKey = Guid.NewGuid().ToString();
        theSession.Events.StartStream<Quest>(theStreamKey, joined, departed);
        return theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task can_fetch_the_stream_version_and_aggregate_type()
    {
        var state = await theSession.Events.FetchStreamStateAsync(theStreamKey);

        state.ShouldNotBeNull();
        state.Key.ShouldBe(theStreamKey);
        state.Version.ShouldBe(2);
        state.AggregateType.ShouldBe(typeof(Quest));
        state.LastTimestamp.ShouldNotBe(DateTimeOffset.MinValue);
        state.Created.ShouldNotBe(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task can_fetch_the_stream_version_and_aggregate_type_async()
    {
        var state = await theSession.Events.FetchStreamStateAsync(theStreamKey);

        state.ShouldNotBeNull();
        state.Key.ShouldBe(theStreamKey);
        state.Version.ShouldBe(2);
        state.AggregateType.ShouldBe(typeof(Quest));
        state.LastTimestamp.ShouldNotBe(DateTimeOffset.MinValue);
        state.Created.ShouldNotBe(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task can_fetch_the_stream_version_through_batch_query()
    {
        var batch = theSession.CreateBatchQuery();

        var stateTask = batch.Events.FetchStreamState(theStreamKey);

        await batch.Execute();

        var state = await stateTask;

        state.Key.ShouldBe(theStreamKey);
        state.Version.ShouldBe(2);
        state.AggregateType.ShouldBe(typeof(Quest));
        state.LastTimestamp.ShouldNotBe(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task can_fetch_the_stream_events_through_batch_query()
    {
        var batch = theSession.CreateBatchQuery();

        var eventsTask = batch.Events.FetchStream(theStreamKey);

        await batch.Execute();

        var events = await eventsTask;

        events.Count.ShouldBe(2);
    }

    [Fact]
    public async Task call_fetch_stream_state_on_new_stream()
    {
        UseStreamIdentity(StreamIdentity.AsGuid);

        Guid id;

        await using (var session = theStore.LightweightSession())
        {
            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            id = session.Events.StartStream<Quest>(joined, departed).Id;
            await session.SaveChangesAsync();
        }

        using (var store2 = SeparateStore())
        {
            await using (var session = store2.LightweightSession())
            {
                var state = await session.Events.FetchStreamStateAsync(id);
                state.Version.ShouldBe(2);
            }
        }
    }


}

