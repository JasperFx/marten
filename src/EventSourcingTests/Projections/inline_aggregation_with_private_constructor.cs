using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Core.Serialization;
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
            _.Projections.Snapshot<QuestMonstersWithPrivateConstructor>(ProjectionLifecycle.Inline);
            _.Projections.Snapshot<QuestMonstersWithNonDefaultPublicConstructor>(ProjectionLifecycle.Inline);
            _.Projections.Snapshot<WithDefaultPrivateConstructorNonDefaultPublicConstructor>(ProjectionLifecycle.Inline);
            _.Projections.Snapshot<WithMultiplePublicNonDefaultConstructors>(ProjectionLifecycle.Inline);
            _.Projections.Snapshot<WithMultiplePrivateNonDefaultConstructors>(ProjectionLifecycle.Inline);
            _.Projections.Snapshot<WithMultiplePrivateNonDefaultConstructorsAndAttribute>(ProjectionLifecycle.Inline);
            _.Projections.Snapshot<WithNonDefaultConstructorsPrivateAndPublicWithEqualParamsCount>(
                ProjectionLifecycle.Inline);
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

    private async Task Verify<T>() where T : IMonstersView
    {
        var slayed1 = new MonsterSlayed { Name = "Troll" };
        var slayed2 = new MonsterSlayed { Name = "Dragon" };
        var streamId = theSession.Events
            .StartStream(slayed1, slayed2).Id;

        await theSession.SaveChangesAsync();

        var loadedView = await theSession.LoadAsync<T>(streamId);

        loadedView.ShouldNotBe(default);
        loadedView!.Id.ShouldBe(streamId);
        loadedView.Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");

        var queriedView = theSession.Query<T>()
            .Single(x => x.Id == streamId);

        queriedView.Id.ShouldBe(streamId);
        queriedView.Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");
    }
}
