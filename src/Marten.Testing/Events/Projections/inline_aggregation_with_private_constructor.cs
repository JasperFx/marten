using System;
using System.Linq;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class inline_aggregation_with_private_constructor: IntegrationContext
    {
        public inline_aggregation_with_private_constructor(DefaultStoreFixture fixture) : base(fixture)
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.UseDefaultSerialization(nonPublicMembersStorage: NonPublicMembersStorage.NonPublicSetters);
                _.Events.Projections.SelfAggregate<QuestMonstersWithPrivateConstructor>();
            });
        }

        [Fact]
        public void run_inline_aggregation_with_private_constructor()
        {
            var slayed1 = new MonsterSlayed { Name = "Troll" };
            var slayed2 = new MonsterSlayed { Name = "Dragon" };
            var streamId = theSession.Events
                .StartStream<QuestMonstersWithPrivateConstructor>(slayed1, slayed2).Id;

            theSession.SaveChanges();

            var loadedView = theSession.Load<QuestMonstersWithPrivateConstructor>(streamId);

            loadedView.Id.ShouldBe(streamId);
            loadedView.Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");

            var queriedView = theSession.Query<QuestMonstersWithPrivateConstructor>()
                .Single(x => x.Id == streamId);

            queriedView.Id.ShouldBe(streamId);
            queriedView.Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");
        }
    }
}
