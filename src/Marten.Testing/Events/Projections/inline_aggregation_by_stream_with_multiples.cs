using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Services;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class inline_aggregation_by_stream_with_multiples : DocumentSessionFixture<NulloIdentityMap>
    {
        QuestStarted started = new QuestStarted {Name = "Find the Orb"};
        MembersJoined joined = new MembersJoined {Day = 2, Location = "Faldor's Farm", Members = new string[] {"Garion", "Polgara", "Belgarath"}};
        AggregatorTests.MonsterSlayed slayed1 = new AggregatorTests.MonsterSlayed {Name = "Troll"};        
        AggregatorTests.MonsterSlayed slayed2 = new AggregatorTests.MonsterSlayed {Name = "Dragon"};  
        
        MembersJoined joined2 = new MembersJoined {Day = 5, Location = "Sendaria", Members = new string[] {"Silk", "Barak"}};

        public inline_aggregation_by_stream_with_multiples()
        {

        }

        [Fact]
        public void run_multiple_aggregates_sync()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.AggregateStreamsInlineWith<QuestParty>();
                _.Events.AggregateStreamsInlineWith<QuestMonsters>();
            });

            var streamId = theSession.Events.StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2);
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
                _.Events.AggregateStreamsInlineWith<QuestParty>();
                _.Events.AggregateStreamsInlineWith<QuestMonsters>();
            });

            var streamId = theSession.Events.StartStream<QuestParty>(started, joined, slayed1, slayed2, joined2);
            await theSession.SaveChangesAsync();

            (await theSession.LoadAsync<QuestMonsters>(streamId)).Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");

            (await theSession.LoadAsync<QuestParty>(streamId)).Members
                .ShouldHaveTheSameElementsAs("Garion", "Polgara", "Belgarath", "Silk", "Barak");
        }
    }

    public class QuestMonsters
    {
        public Guid Id { get; set; }

        private readonly IList<string> _monsters = new List<string>();

        public void Apply(AggregatorTests.MonsterSlayed slayed)
        {
            _monsters.Fill(slayed.Name);
        }

        public string[] Monsters
        {
            get { return _monsters.ToArray(); }
            set
            {
                _monsters.Clear();
                _monsters.AddRange(value);
            }
        }
    }
}