using System.Linq;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Projections;

public class inline_aggregation_with_private_constructor: OneOffConfigurationsContext
{
    public inline_aggregation_with_private_constructor()
    {
        StoreOptions(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.All;
            _.UseDefaultSerialization(nonPublicMembersStorage: NonPublicMembersStorage.All);
            _.Projections.SelfAggregate<QuestMonstersWithPrivateConstructor>();
            _.Projections.SelfAggregate<QuestMonstersWithNonDefaultPublicConstructor>();
            _.Projections.SelfAggregate<WithDefaultPrivateConstructorNonDefaultPublicConstructor>();
            _.Projections.SelfAggregate<WithMultiplePublicNonDefaultConstructors>();
            _.Projections.SelfAggregate<WithMultiplePrivateNonDefaultConstructors>();
            _.Projections.SelfAggregate<WithMultiplePrivateNonDefaultConstructorsAndAttribute>();
            _.Projections.SelfAggregate<WithNonDefaultConstructorsPrivateAndPublicWithEqualParamsCount>();
        });
    }

    [Fact]
    public void run_inline_aggregation_with_private_constructor()
    {
        Verify<QuestMonstersWithPrivateConstructor>();
        Verify<QuestMonstersWithNonDefaultPublicConstructor>();
        Verify<WithDefaultPrivateConstructorNonDefaultPublicConstructor>();
        Verify<WithMultiplePublicNonDefaultConstructors>();
        Verify<WithMultiplePrivateNonDefaultConstructors>();
        Verify<WithMultiplePrivateNonDefaultConstructorsAndAttribute>();
        Verify<WithNonDefaultConstructorsPrivateAndPublicWithEqualParamsCount>();
    }

    private void Verify<T>() where T: IMonstersView
    {
        var slayed1 = new MonsterSlayed { Name = "Troll" };
        var slayed2 = new MonsterSlayed { Name = "Dragon" };
        var streamId = theSession.Events
            .StartStream(slayed1, slayed2).Id;

        theSession.SaveChanges();

        var loadedView = theSession.Load<T>(streamId);

        loadedView.ShouldNotBeNull();
        loadedView!.Id.ShouldBe(streamId);
        loadedView.Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");

        var queriedView = theSession.Query<T>()
            .Single(x => x.Id == streamId);

        queriedView.Id.ShouldBe(streamId);
        queriedView.Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");
    }
}