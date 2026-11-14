using System;
using System.Collections.Generic;
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
    // discarded Task and the test always passed. Awaited, it showed that several of these aggregates had gone stale in
    // Marten 9: with no PUBLIC parameterless constructor they are created without running a constructor, so their
    // initializers never ran and Apply() hit a null list. They now initialize in Apply() instead (see QuestMonsters.cs),
    // and the documented behaviour itself is pinned below.
    [Fact]
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

    // Pins the documented Marten 9/10 behaviour (migration guide, "Handler methods must be public"): an aggregate
    // with no public parameterless constructor is created without running any constructor, so a field initializer
    // has not run by the first Apply(). JasperFx/jasperfx#1188 proposes having the generated evolver call the private
    // constructor through [UnsafeAccessor] instead. When jasperfx#1188 lands, this assertion flips.
    [Fact]
    public async Task an_aggregate_without_a_public_parameterless_constructor_is_built_without_its_initializers()
    {
        var streamId = theSession.Events.StartStream(new MonsterSlayed { Name = "Troll" }).Id;
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<AggregateBuiltWithoutItsConstructor>(streamId);

        aggregate.ShouldNotBeNull();
        aggregate.Applied.ShouldBe(1);

        // When jasperfx#1188 lands, this assertion flips: ShouldBeTrue()
        aggregate.InitializerHadRunByFirstApply.ShouldBeFalse();
    }
}

public class AggregateBuiltWithoutItsConstructor
{
    private AggregateBuiltWithoutItsConstructor()
    {
    }

    public Guid Id { get; set; }

    private readonly List<string> _initializedByFieldInitializer = new();

    public bool InitializerHadRunByFirstApply { get; set; }

    public int Applied { get; set; }

    public void Apply(MonsterSlayed slayed)
    {
        if (Applied == 0)
        {
            InitializerHadRunByFirstApply = _initializedByFieldInitializer != null;
        }

        Applied++;
    }
}
