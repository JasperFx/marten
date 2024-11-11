using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections;

public class inline_aggregation_with_base_view_class: OneOffConfigurationsContext, IAsyncLifetime
{
    private readonly MonsterSlayed slayed1 = new() { Name = "Troll" };
    private readonly MonsterSlayed slayed2 = new() { Name = "Dragon" };
    private Guid streamId;

    public inline_aggregation_with_base_view_class()
    {
        StoreOptions(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.All;
            _.Projections.Snapshot<QuestMonstersWithBaseClass>(ProjectionLifecycle.Inline);
            _.Projections.Snapshot<QuestMonstersWithBaseClassAndIdOverloaded>(ProjectionLifecycle.Inline);
            _.Projections.Snapshot<QuestMonstersWithBaseClassAndIdOverloadedWithNew>(ProjectionLifecycle.Inline);
        });
    }

    public async Task InitializeAsync()
    {
        streamId = theSession.Events
            .StartStream<QuestMonstersWithBaseClass>(slayed1, slayed2).Id;

        await theSession.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task run_inline_aggregation_with_base_view_class()
    {
        return VerifyProjection<QuestMonstersWithBaseClass>();
    }

    [Fact]
    public Task run_inline_aggregation_with_base_class_and_id_overloaded()
    {
        return VerifyProjection<QuestMonstersWithBaseClassAndIdOverloaded>();
    }

    [Fact]
    public Task run_inline_aggregation_with_base_class_and_id_overloaded_with_new()
    {
        return VerifyProjection<QuestMonstersWithBaseClassAndIdOverloadedWithNew>();
    }

    private async Task VerifyProjection<T>() where T : IMonstersView
    {
        var loadedView = await theSession.LoadAsync<T>(streamId);

        loadedView.ShouldNotBe(default);
        loadedView.Id.ShouldBe(streamId);
        loadedView.Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");

        var queriedView = theSession.Query<T>()
            .Single(x => x.Id == streamId);

        queriedView.Id.ShouldBe(streamId);
        queriedView.Monsters.ShouldHaveTheSameElementsAs("Troll", "Dragon");
    }
}
