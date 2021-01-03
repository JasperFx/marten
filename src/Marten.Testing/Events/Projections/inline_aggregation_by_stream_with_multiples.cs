using System.Threading.Tasks;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class inline_aggregation_by_stream_with_multiples: IntegrationContext
    {
        private readonly QuestStarted started = new QuestStarted { Name = "Find the Orb" };
        private readonly MembersJoined joined = new MembersJoined { Day = 2, Location = "Faldor's Farm", Members = new string[] { "Garion", "Polgara", "Belgarath" } };
        private readonly MonsterSlayed slayed1 = new MonsterSlayed { Name = "Troll" };
        private readonly MonsterSlayed slayed2 = new MonsterSlayed { Name = "Dragon" };

        private readonly MembersJoined joined2 = new MembersJoined { Day = 5, Location = "Sendaria", Members = new string[] { "Silk", "Barak" } };

        [Theory]
        [InlineData(TenancyStyle.Single)]
        [InlineData(TenancyStyle.Conjoined)]
        public void run_multiple_aggregates_sync(TenancyStyle tenancyStyle)
        {
            // SAMPLE: registering-quest-party
            var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Events.TenancyStyle = tenancyStyle;
                _.DatabaseSchemaName = "quest_sample";

                // This is all you need to create the QuestParty projected
                // view
                _.Events.Projections.InlineSelfAggregate<QuestParty>();
            });
            // ENDSAMPLE

            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.Projections.InlineSelfAggregate<QuestParty>();
                _.Events.Projections.InlineSelfAggregate<QuestMonsters>();
            });

            var streamId = theSession.Events
                .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
            theSession.SaveChanges();

            theSession.Load<QuestMonsters>(streamId).Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");

            theSession.Load<QuestParty>(streamId).Members
                .ShouldHaveTheSameElementsAs("Garion", "Polgara", "Belgarath", "Silk", "Barak");
        }

        [Fact]
        public async Task run_multiple_aggregates_async()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.Events.Projections.InlineSelfAggregate<QuestParty>();
                _.Events.Projections.InlineSelfAggregate<QuestMonsters>();
            });

            var streamId = theSession.Events
                .StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2).Id;
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            (await theSession.LoadAsync<QuestMonsters>(streamId).ConfigureAwait(false)).Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");

            (await theSession.LoadAsync<QuestParty>(streamId).ConfigureAwait(false)).Members
                .ShouldHaveTheSameElementsAs("Garion", "Polgara", "Belgarath", "Silk", "Barak");
        }

        public inline_aggregation_by_stream_with_multiples(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
