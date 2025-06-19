using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Projections;

public class inline_aggregation_by_stream_with_multiples: OneOffConfigurationsContext
{
    private readonly QuestStarted started = new QuestStarted { Name = "Find the Orb" };

    private readonly MembersJoined joined = new MembersJoined
    {
        Day = 2, Location = "Faldor's Farm", Members = ["Garion", "Polgara", "Belgarath"]
    };

    private readonly MonsterSlayed slayed1 = new MonsterSlayed { Name = "Troll" };
    private readonly MonsterSlayed slayed2 = new MonsterSlayed { Name = "Dragon" };

    private readonly MembersJoined joined2 =
        new MembersJoined { Day = 5, Location = "Sendaria", Members = ["Silk", "Barak"] };

    [Theory]
    [InlineData(TenancyStyle.Single)]
    [InlineData(TenancyStyle.Conjoined)]
    public async Task run_multiple_aggregates_sync(TenancyStyle tenancyStyle)
    {
        #region sample_registering-quest-party

        var store = DocumentStore.For(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);
            _.Events.TenancyStyle = tenancyStyle;
            _.DatabaseSchemaName = "quest_sample";
            if (tenancyStyle == TenancyStyle.Conjoined)
            {
                _.Schema.For<QuestParty>().MultiTenanted();
            }

            // This is all you need to create the QuestParty projected
            // view
            _.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Inline);
        });

        #endregion

        StoreOptions(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<QuestMonsters>(SnapshotLifecycle.Inline);
        });

        var streamId = theSession.Events
            .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<QuestMonsters>(streamId)).Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");

        (await theSession.LoadAsync<QuestParty>(streamId)).Members
            .ShouldHaveTheSameElementsAs("Garion", "Polgara", "Belgarath", "Silk", "Barak");
    }

    [Fact]
    public async Task run_multiple_aggregates_async()
    {
        StoreOptions(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.All;

            _.Projections.Snapshot<QuestMonsters>(SnapshotLifecycle.Inline);
            _.Projections.Snapshot<QuestParty>(SnapshotLifecycle.Inline);
        });

        var streamId = theSession.Events
            .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<QuestMonsters>(streamId)).Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");

        (await theSession.LoadAsync<QuestParty>(streamId)).Members
            .ShouldHaveTheSameElementsAs("Garion", "Polgara", "Belgarath", "Silk", "Barak");
    }
}
