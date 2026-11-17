using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;
using Marten.Newtonsoft;

namespace EventSourcingTests.Projections;

public partial class inline_aggregation_with_private_constructor: OneOffConfigurationsContext
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

            // A generic document and one nested in a private type cannot carry their own Apply() methods: the
            // generator's self-aggregating evolver is a file-scoped type that can name neither. They are built by a
            // projection instead, which is where JasperFx 3.3.0 still uses GetUninitializedObject for them.
            _.Projections.Add(new GenericDocWithPrivateConstructorProjection(), ProjectionLifecycle.Live);
            _.Projections.Add(new PrivateHolder.NestedDocProjection(), ProjectionLifecycle.Live);
        });
    }

    // Until #5861 this test called the async Verify<T>() without awaiting it, so every failure inside it landed in a
    // discarded Task and the test always passed. Awaited, it showed that in Marten 9 and the 10.0 alphas these
    // aggregates were created without running a constructor, so their initializers never ran and Apply() hit a null
    // list. From JasperFx 3.3.0 (jasperfx#1188) the generated evolver calls a non-public parameterless constructor
    // through [UnsafeAccessor], so the aggregates in QuestMonsters.cs rely on their field initializers again.
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

    // JasperFx 3.3.0 (jasperfx#1188, jasperfx#1191): an aggregate whose only parameterless constructor is private is
    // built by calling that constructor through a generated [UnsafeAccessor], so the constructor body and every
    // initializer have run by the first Apply(), as in Marten 8. Marten 9 and the 10.0 alphas built it with
    // RuntimeHelpers.GetUninitializedObject, and this test pinned that the initializer had NOT run.
    [Fact]
    public async Task an_aggregate_with_a_private_parameterless_constructor_runs_it_and_its_initializers()
    {
        var aggregate = await aggregateOneEvent<AggregateWithPrivateConstructor>();

        aggregate.Applied.ShouldBe(1);
        aggregate.ConstructorHadRunByFirstApply.ShouldBeTrue();
        aggregate.InitializerHadRunByFirstApply.ShouldBeTrue();
    }

    [Fact]
    public async Task an_aggregate_with_a_protected_parameterless_constructor_runs_it_and_its_initializers()
    {
        var aggregate = await aggregateOneEvent<AggregateWithProtectedConstructor>();

        aggregate.Applied.ShouldBe(1);
        aggregate.ConstructorHadRunByFirstApply.ShouldBeTrue();
        aggregate.InitializerHadRunByFirstApply.ShouldBeTrue();
    }

    // The three shapes JasperFx 3.3.0 still builds with RuntimeHelpers.GetUninitializedObject, so no constructor and no
    // initializer runs. Pinned so a later JasperFx that changes any of them shows up here (and in the migration guide).

    // No parameterless constructor at all: there is nothing to call.
    [Fact]
    public async Task an_aggregate_with_no_parameterless_constructor_is_built_without_its_initializers()
    {
        var aggregate = await aggregateOneEvent<AggregateWithNoParameterlessConstructor>();

        aggregate.Applied.ShouldBe(1);
        aggregate.InitializerHadRunByFirstApply.ShouldBeFalse();
    }

    // A record with a primary constructor, the common case of the above.
    [Fact]
    public async Task a_record_with_a_primary_constructor_is_built_without_its_initializers()
    {
        var aggregate = await aggregateOneEvent<RecordAggregateWithPrimaryConstructor>();

        aggregate.Applied.ShouldBe(1);
        aggregate.InitializerHadRunByFirstApply.ShouldBeFalse();
    }

    // A generic aggregate: the file-scoped accessor cannot name it.
    [Fact]
    public async Task a_generic_aggregate_with_a_private_constructor_is_built_without_its_initializers()
    {
        var aggregate = await aggregateOneEvent<GenericDocWithPrivateConstructor<int>>();

        aggregate.Applied.ShouldBe(1);
        aggregate.InitializerHadRunByFirstApply.ShouldBeFalse();
    }

    // Nested inside a private type: the file-scoped accessor cannot reach it.
    [Fact]
    public async Task an_aggregate_nested_in_a_private_type_is_built_without_its_initializers()
    {
        var aggregate = await aggregateOneEvent<PrivateHolder.NestedDocWithPrivateConstructor>();

        aggregate.Applied.ShouldBe(1);
        aggregate.InitializerHadRunByFirstApply.ShouldBeFalse();
    }

    private async Task<T> aggregateOneEvent<T>() where T : class
    {
        var streamId = theSession.Events.StartStream(new MonsterSlayed { Name = "Troll" }).Id;
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Events.AggregateStreamAsync<T>(streamId);
        aggregate.ShouldNotBeNull();
        return aggregate;
    }

    private partial class PrivateHolder
    {
        public class NestedDocWithPrivateConstructor
        {
            private NestedDocWithPrivateConstructor()
            {
            }

            public Guid Id { get; set; }

            private readonly List<string> _initializedByFieldInitializer = new();

            public bool InitializerHasRun => _initializedByFieldInitializer != null;

            public bool InitializerHadRunByFirstApply { get; set; }

            public int Applied { get; set; }
        }

        public partial class NestedDocProjection: SingleStreamProjection<NestedDocWithPrivateConstructor, Guid>
        {
            public void Apply(MonsterSlayed slayed, NestedDocWithPrivateConstructor doc)
            {
                if (doc.Applied == 0)
                {
                    doc.InitializerHadRunByFirstApply = doc.InitializerHasRun;
                }

                doc.Applied++;
            }
        }
    }
}

public class AggregateWithPrivateConstructor
{
    private AggregateWithPrivateConstructor()
    {
        _constructorRan = true;
    }

    public Guid Id { get; set; }

    private readonly bool _constructorRan;

    private readonly List<string> _initializedByFieldInitializer = new();

    public bool ConstructorHadRunByFirstApply { get; set; }

    public bool InitializerHadRunByFirstApply { get; set; }

    public int Applied { get; set; }

    public void Apply(MonsterSlayed slayed)
    {
        if (Applied == 0)
        {
            ConstructorHadRunByFirstApply = _constructorRan;
            InitializerHadRunByFirstApply = _initializedByFieldInitializer != null;
        }

        Applied++;
    }
}

public class AggregateWithProtectedConstructor
{
    protected AggregateWithProtectedConstructor()
    {
        _constructorRan = true;
    }

    public Guid Id { get; set; }

    private readonly bool _constructorRan;

    private readonly List<string> _initializedByFieldInitializer = new();

    public bool ConstructorHadRunByFirstApply { get; set; }

    public bool InitializerHadRunByFirstApply { get; set; }

    public int Applied { get; set; }

    public void Apply(MonsterSlayed slayed)
    {
        if (Applied == 0)
        {
            ConstructorHadRunByFirstApply = _constructorRan;
            InitializerHadRunByFirstApply = _initializedByFieldInitializer != null;
        }

        Applied++;
    }
}

public class AggregateWithNoParameterlessConstructor
{
    public AggregateWithNoParameterlessConstructor(Guid id)
    {
        Id = id;
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

public record RecordAggregateWithPrimaryConstructor(Guid Id)
{
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

public class GenericDocWithPrivateConstructor<T>
{
    private GenericDocWithPrivateConstructor()
    {
    }

    public Guid Id { get; set; }

    public T? Payload { get; set; }

    private readonly List<string> _initializedByFieldInitializer = new();

    public bool InitializerHasRun => _initializedByFieldInitializer != null;

    public bool InitializerHadRunByFirstApply { get; set; }

    public int Applied { get; set; }
}

public partial class GenericDocWithPrivateConstructorProjection: SingleStreamProjection<GenericDocWithPrivateConstructor<int>, Guid>
{
    public void Apply(MonsterSlayed slayed, GenericDocWithPrivateConstructor<int> doc)
    {
        if (doc.Applied == 0)
        {
            doc.InitializerHadRunByFirstApply = doc.InitializerHasRun;
        }

        doc.Applied++;
    }
}
