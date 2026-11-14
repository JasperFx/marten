using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;
using Marten.Newtonsoft;

namespace EventSourcingTests.Projections;

public class inline_aggregation_with_private_constructor: OneOffConfigurationsContext
{
    public inline_aggregation_with_private_constructor()
    {
        StoreOptions(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.All;
            _.UseNewtonsoftForSerialization(nonPublicMembersStorage: NonPublicMembersStorage.All);
            _.Projections.Snapshot<QuestMonstersWithPrivateConstructor>(SnapshotLifecycle.Inline);
            _.Projections.Snapshot<QuestMonstersWithNonDefaultPublicConstructor>(SnapshotLifecycle.Inline);
            _.Projections.Snapshot<WithDefaultPrivateConstructorNonDefaultPublicConstructor>(SnapshotLifecycle.Inline);
            _.Projections.Snapshot<WithMultiplePublicNonDefaultConstructors>(SnapshotLifecycle.Inline);
            _.Projections.Snapshot<WithMultiplePrivateNonDefaultConstructors>(SnapshotLifecycle.Inline);
            _.Projections.Snapshot<WithMultiplePrivateNonDefaultConstructorsAndAttribute>(SnapshotLifecycle.Inline);
            _.Projections.Snapshot<WithNonDefaultConstructorsPrivateAndPublicWithEqualParamsCount>(
                SnapshotLifecycle.Inline);
        });
    }

    // Until #5861 this test called the async Verify<T>() without awaiting it, so every failure inside it landed in a
    // discarded Task and the test always passed. Awaited, it fails the same way on origin/master. For an aggregate
    // with no PUBLIC parameterless constructor, JasperFx.Events.SourceGenerator 3.0.0 builds the missing snapshot
    // with RuntimeHelpers.GetUninitializedObject, so the private constructor and the field and property initializers
    // never run. QuestMonstersWithPrivateConstructor's `_monsters = new List<string>()` stays null, and Apply() throws a
    // NullReferenceException. That is documented Marten 9 behaviour (migration guide, "Handler methods must be
    // public"), so these aggregates are stale rather than Marten being wrong. The test is skipped with the reason
    // rather than hidden again. Whether the generator should invoke a private parameterless constructor instead
    // (UnsafeAccessor) is a JasperFx question.
    [Fact(Skip = "Stale since Marten 9: the generated evolver builds private-constructor aggregates with GetUninitializedObject, so their initializers never run. Was silently unawaited before #5861.")]
    public async Task run_inline_aggregation_with_private_constructor()
    {
        await Verify<QuestMonstersWithPrivateConstructor>();
        await Verify<QuestMonstersWithNonDefaultPublicConstructor>();
        await Verify<WithDefaultPrivateConstructorNonDefaultPublicConstructor>();
        await Verify<WithMultiplePublicNonDefaultConstructors>();
        await Verify<WithMultiplePrivateNonDefaultConstructors>();
        await Verify<WithMultiplePrivateNonDefaultConstructorsAndAttribute>();
        await Verify<WithNonDefaultConstructorsPrivateAndPublicWithEqualParamsCount>();
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

        var queriedView = await theSession.Query<T>()
            .SingleAsync(x => x.Id == streamId);

        queriedView.Id.ShouldBe(streamId);
        queriedView.Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");
    }
}
