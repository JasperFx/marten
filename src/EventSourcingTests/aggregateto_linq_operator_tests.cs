using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Projections;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class aggregateTo_linq_operator_tests: DestructiveIntegrationContext
{
    private readonly MembersJoined _joined1 = new() { Members = new[] { "Rand", "Matrim", "Perrin", "Thom" } };
    private readonly MembersDeparted _departed1 = new() { Members = new[] {"Thom"} };

    private readonly MembersJoined _joined2 = new() { Members = new[] {"Elayne", "Moiraine", "Elmindreda"} };
    private readonly MembersDeparted _departed2 = new() { Members = new[] {"Moiraine"} };

    [Fact]
    public async Task can_aggregate_events_to_aggregate_type_asynchronously()
    {
        theSession.Events.StartStream<Quest>(_joined1, _departed1);
        theSession.Events.StartStream<Quest>(_joined2, _departed2);
        await theSession.SaveChangesAsync();

        #region sample_aggregateto_async_usage_with_linq

        var questParty = await theSession.Events
            .QueryAllRawEvents()

            // You could of course chain all the Linq
            // Where()/OrderBy()/Take()/Skip() operators
            // you need here

            .AggregateToAsync<QuestParty>();

        #endregion

        questParty.Members
            .ShouldHaveTheSameElementsAs("Rand", "Matrim", "Perrin", "Elayne", "Elmindreda");
    }

    [Fact]
    public async Task can_aggregate_with_initial_state_asynchronously()
    {
        var initialParty = new QuestParty { Members = new List<string> { "Lan" } };
        theSession.Events.StartStream<Quest>(_joined1, _departed1);
        theSession.Events.StartStream<Quest>(_joined2, _departed2);
        await theSession.SaveChangesAsync();

        var questParty = await theSession.Events.QueryAllRawEvents().AggregateToAsync(initialParty);

        questParty.Members.ShouldHaveTheSameElementsAs("Lan", "Rand", "Matrim", "Perrin", "Elayne", "Elmindreda");
    }

    [Fact]
    public async Task gets_the_id_set()
    {
        var initialParty = new QuestParty { Members = new List<string> { "Lan" } };
        var id = theSession.Events.StartStream<Quest>(_joined1, _departed1).Id;
        theSession.Events.StartStream<Quest>(_joined2, _departed2);
        await theSession.SaveChangesAsync();

        var questParty = await theSession.Events.QueryAllRawEvents().Where(x => x.StreamId == id).AggregateToAsync(initialParty);
        questParty.Id.ShouldBe(id);
    }

    [Fact]
    public async Task gets_the_key_set()
    {
        UseStreamIdentity(StreamIdentity.AsString);

        var key = Guid.NewGuid().ToString();

        theSession.Events.StartStream<Quest>(key, _joined1, _departed1);
        theSession.Events.StartStream<Quest>(Guid.NewGuid().ToString(),_joined2, _departed2);
        await theSession.SaveChangesAsync();

        var questParty = await theSession.Events.QueryAllRawEvents().Where(x => x.StreamKey == key).AggregateToAsync<QuestPartyWithStringIdentifier>(null);
        questParty.Id.ShouldBe(key);
    }

    public aggregateTo_linq_operator_tests(DefaultStoreFixture fixture): base(fixture)
    {
        theStore.Advanced.Clean.DeleteAllEventDataAsync().GetAwaiter().GetResult();
    }
}
