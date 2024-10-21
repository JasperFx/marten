using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Projections;

public class inline_aggregation_with_non_public_setter: OneOffConfigurationsContext, IAsyncLifetime
{
    private readonly MonsterSlayed slayed1 = new MonsterSlayed { Name = "Troll" };
    private readonly MonsterSlayed slayed2 = new MonsterSlayed { Name = "Dragon" };
    private Guid streamId;

    public inline_aggregation_with_non_public_setter()
    {
        StoreOptions(opts =>
        {
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.UseDefaultSerialization(nonPublicMembersStorage: NonPublicMembersStorage.NonPublicSetters);
            opts.Projections.Snapshot<QuestMonstersWithPrivateIdSetter>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<QuestMonstersWithProtectedIdSetter>(SnapshotLifecycle.Inline);
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
    public void run_inline_aggregation_with_private_id_setter()
    {
        VerifyProjection<QuestMonstersWithPrivateIdSetter>();
    }

    [Fact]
    public void run_inline_aggregation_with_protected_id_setter()
    {
        VerifyProjection<QuestMonstersWithProtectedIdSetter>();
    }

    private void VerifyProjection<T>() where T : IMonstersView
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
