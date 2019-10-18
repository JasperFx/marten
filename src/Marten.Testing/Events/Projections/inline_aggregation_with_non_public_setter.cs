using System;
using System.Linq;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class inline_aggregation_with_non_public_setter: DocumentSessionFixture<NulloIdentityMap>
    {
        private readonly MonsterSlayed slayed1 = new MonsterSlayed { Name = "Troll" };
        private readonly MonsterSlayed slayed2 = new MonsterSlayed { Name = "Dragon" };
        private readonly Guid streamId;

        public inline_aggregation_with_non_public_setter()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.UseDefaultSerialization(nonPublicMembersStorage: NonPublicMembersStorage.NonPublicSetters);
                _.Events.InlineProjections.AggregateStreamsWith<QuestMonstersWithPrivateIdSetter>();
                _.Events.InlineProjections.AggregateStreamsWith<QuestMonstersWithProtectedIdSetter>();
            });

            streamId = theSession.Events
                .StartStream<QuestMonstersWithBaseClass>(slayed1, slayed2).Id;

            theSession.SaveChanges();
        }

        [Fact]
        public void run_inline_aggregation_with_private_id_setter()
        {
            VerifyInlineProjection<QuestMonstersWithPrivateIdSetter>();
        }

        [Fact]
        public void run_inline_aggregation_with_protected_id_setter()
        {
            VerifyInlineProjection<QuestMonstersWithProtectedIdSetter>();
        }

        private void VerifyInlineProjection<T>() where T : IMonstersView
        {
            var loadedView = theSession.Load<T>(streamId);

            loadedView.Id.ShouldBe(streamId);
            loadedView.Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");

            var queriedView = theSession.Query<T>()
                .Single(x => x.Id == streamId);

            queriedView.Id.ShouldBe(streamId);
            queriedView.Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");
        }
    }
}
