using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Projections;

public class inline_transformation_of_events: OneOffConfigurationsContext
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

    private async Task sample_usage()
    {
        #region sample_usage_of_inline_projection

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            opts.Projections.Add(new MonsterDefeatedTransform(),
                ProjectionLifecycle.Inline);
        });

        await using var session = store.LightweightSession();

        var streamId = session.Events
            .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;

        // The projection is going to be applied right here during
        // the call to SaveChangesAsync() and the resulting document update
        // of the new MonsterDefeated document will happen in the same database
        // transaction
        await theSession.SaveChangesAsync();

        #endregion
    }

    [Theory]
    [InlineData(TenancyStyle.Single)]
    [InlineData(TenancyStyle.Conjoined)]
    public async Task sync_projection_of_events(TenancyStyle tenancyStyle)
    {
        StoreOptions(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.All;
            _.Events.TenancyStyle = tenancyStyle;

            _.Projections.Add(new MonsterDefeatedTransform(), ProjectionLifecycle.Inline);
        });

        var streamId = theSession.Events
            .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
        await theSession.SaveChangesAsync();

        var monsterEvents =
            (await theSession.Events.FetchStreamAsync(streamId)).OfType<Event<MonsterSlayed>>().ToArray();

        monsterEvents.Length.ShouldBe(2); // precondition

        monsterEvents.Each(e =>
        {
            var doc = theSession.Load<MonsterDefeated>(e.Id);
            doc.Monster.ShouldBe(e.Data.Name);
        });
    }

    [Fact]
    public async Task sync_projection_of_events_with_direct_configuration()
    {
        StoreOptions(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.All;

            _.Projections.Add(new MonsterDefeatedTransform(), ProjectionLifecycle.Inline);
        });

        var streamId = theSession.Events
            .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
        await theSession.SaveChangesAsync();

        var monsterEvents =
            (await theSession.Events.FetchStreamAsync(streamId)).OfType<Event<MonsterSlayed>>().ToArray();

        monsterEvents.Length.ShouldBe(2); // precondition

        monsterEvents.Each(e =>
        {
            var doc = theSession.Load<MonsterDefeated>(e.Id);
            doc.Monster.ShouldBe(e.Data.Name);
        });
    }

    [Fact]
    public async Task async_projection_of_events()
    {
        #region sample_applying-monster-defeated

        var store = DocumentStore.For(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);
            _.DatabaseSchemaName = "monster_defeated";

            _.Projections.Add(new MonsterDefeatedTransform(), ProjectionLifecycle.Inline);
        });

        #endregion

        // The code below is just customizing the document store
        // used in the tests
        StoreOptions(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.All;

            _.Projections.Add(new MonsterDefeatedTransform(), ProjectionLifecycle.Inline);
        });

        var streamId = theSession.Events
            .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
        await theSession.SaveChangesAsync();

        var monsterEvents =
            (await theSession.Events.FetchStreamAsync(streamId)).OfType<Event<MonsterSlayed>>().ToArray();

        monsterEvents.Length.ShouldBe(2); // precondition

        foreach (var e in monsterEvents)
        {
            var doc = await theSession.LoadAsync<MonsterDefeated>(e.Id);
            doc.Monster.ShouldBe(e.Data.Name);
        }
    }

    public inline_transformation_of_events()
    {
    }
}

#region sample_MonsterDefeatedTransform

public class MonsterDefeatedTransform: EventProjection
{
    public MonsterDefeated Create(IEvent<MonsterSlayed> input)
    {
        return new MonsterDefeated { Id = input.Id, Monster = input.Data.Name };
    }
}

public class MonsterDefeated
{
    public Guid Id { get; set; }
    public string Monster { get; set; }
}

#endregion
