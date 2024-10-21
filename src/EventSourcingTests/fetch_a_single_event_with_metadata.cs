using System;
using System.Threading.Tasks;
using EventSourcingTests.Projections;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class fetch_a_single_event_with_metadata: IntegrationContext
{
    private readonly QuestStarted started = new QuestStarted { Name = "Find the Orb" };

    private readonly MembersJoined joined = new MembersJoined
    {
        Day = 2, Location = "Faldor's Farm", Members = new string[] { "Garion", "Polgara", "Belgarath" }
    };

    private readonly MonsterSlayed slayed1 = new MonsterSlayed { Name = "Troll" };
    private readonly MonsterSlayed slayed2 = new MonsterSlayed { Name = "Dragon" };

    private readonly MembersJoined joined2 =
        new MembersJoined { Day = 5, Location = "Sendaria", Members = new string[] { "Silk", "Barak" } };

    [Fact]
    public async Task fetch_with_metadata_synchronously()
    {
        StoreOptions(x =>
        {
            x.Events.MetadataConfig.HeadersEnabled = true;
            x.Events.MetadataConfig.CausationIdEnabled = true;
            x.Events.MetadataConfig.CorrelationIdEnabled = true;
        });

        theSession.CorrelationId = "The Correlation";
        theSession.CausationId = "The Cause";
        theSession.LastModifiedBy = "Last Person";
        theSession.SetHeader("HeaderKey", "HeaderValue");

        var streamId = theSession.Events
            .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
        await theSession.SaveChangesAsync();

        var events = theSession.Events.FetchStream(streamId);
        events.Count.ShouldBe(5);
        events.ShouldAllBe(e =>
            e.Headers != null && e.Headers.ContainsKey("HeaderKey") && "HeaderValue".Equals(e.Headers["HeaderKey"]));
        events.ShouldAllBe(e => e.CorrelationId == "The Correlation");
        events.ShouldAllBe(e => e.CausationId == "The Cause");
    }

    [Fact]
    public async Task fetch_with_metadata_asynchronously()
    {
        StoreOptions(x =>
        {
            x.Events.MetadataConfig.HeadersEnabled = true;
            x.Events.MetadataConfig.CausationIdEnabled = true;
            x.Events.MetadataConfig.CorrelationIdEnabled = true;
        });

        theSession.CorrelationId = "The Correlation";
        theSession.CausationId = "The Cause";
        theSession.LastModifiedBy = "Last Person";
        theSession.SetHeader("HeaderKey", "HeaderValue");

        var streamId = theSession.Events
            .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(5);
        events.ShouldAllBe(e =>
            e.Headers != null && e.Headers.ContainsKey("HeaderKey") && "HeaderValue".Equals(e.Headers["HeaderKey"]));
        events.ShouldAllBe(e => e.CorrelationId == "The Correlation");
        events.ShouldAllBe(e => e.CausationId == "The Cause");
    }

    [Fact]
    public async Task fetch_synchronously()
    {
        var streamId = theSession.Events
            .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamId);

        theSession.Events.Load(Guid.NewGuid()).ShouldBeNull();

        // Knowing the event type
        var slayed1_2 = theSession.Events.Load<MonsterSlayed>(events[2].Id);
        slayed1_2.Version.ShouldBe(3);
        slayed1_2.Data.Name.ShouldBe("Troll");

        // Not knowing the event type
        var slayed1_3 = theSession.Events.Load<MonsterSlayed>(events[2].Id).ShouldBeOfType<Event<MonsterSlayed>>();
        slayed1_3.Version.ShouldBe(3);
        slayed1_3.Data.Name.ShouldBe("Troll");
    }

    [Fact]
    public async Task fetch_asynchronously()
    {
        var streamId = theSession.Events
            .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamId);

        (await theSession.Events.LoadAsync(Guid.NewGuid())).ShouldBeNull();
        (await theSession.Events.LoadAsync<MonsterSlayed>(Guid.NewGuid())).ShouldBeNull();

        // Knowing the event type
        var slayed1_2 = await theSession.Events.LoadAsync<MonsterSlayed>(events[2].Id);
        slayed1_2.Version.ShouldBe(3);
        slayed1_2.Data.Name.ShouldBe("Troll");

        // Not knowing the event type
        var slayed1_3 = (await theSession.Events.LoadAsync<MonsterSlayed>(events[2].Id))
            .ShouldBeOfType<Event<MonsterSlayed>>();
        slayed1_3.Version.ShouldBe(3);
        slayed1_3.Data.Name.ShouldBe("Troll");
    }

    [Fact]
    public async Task fetch_in_batch_query()
    {
        var streamId = theSession.Events
            .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
        await theSession.SaveChangesAsync();

        var events = await theSession.Events.FetchStreamAsync(streamId);

        var batch = theSession.CreateBatchQuery();

        var slayed1_2 = batch.Events.Load(events[2].Id);
        var slayed2_2 = batch.Events.Load(events[3].Id);
        var missing = batch.Events.Load(Guid.NewGuid());

        await batch.Execute();

        (await slayed1_2).ShouldBeOfType<Event<MonsterSlayed>>()
            .Data.Name.ShouldBe("Troll");

        (await slayed2_2).ShouldBeOfType<Event<MonsterSlayed>>()
            .Data.Name.ShouldBe("Dragon");

        (await missing).ShouldBeNull();
    }

    public fetch_a_single_event_with_metadata(DefaultStoreFixture fixture): base(fixture)
    {
    }
}
