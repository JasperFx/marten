using System;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Metadata;
using Marten.Storage;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.AsyncDaemon.Testing.Bugs;

public class Bug_DeleteWhere_Operations_Should_Respect_Tenancy : BugIntegrationContext
{

    [Fact]
    public async Task ShouldDelete_ForDeleteCondition_AfterRebuild()
    {
        StoreOptions(_ =>
        {
            _.Projections.Add<DeletableEventProjection>(ProjectionLifecycle.Inline);
            _.Events.TenancyStyle = TenancyStyle.Conjoined;
            _.Policies.AllDocumentsAreMultiTenanted();
        });

        var innerGuid = Guid.NewGuid();
        var createNormal = new CreateDeletableProjection(Guid.NewGuid(), innerGuid);
        var deleteNormal = new DeleteEvent(innerGuid);

        var innerGuid2 = Guid.NewGuid();
        var createHard = new CreateDeletableProjection(Guid.NewGuid(), innerGuid2);
        var deleteHard = new HardDeleteEvent(innerGuid2);

        await using var session = theStore.LightweightSession("test");

        theSession.Events.StartStream(createNormal);
        theSession.Events.StartStream(createHard);

        await theSession.SaveChangesAsync();

        var createdProjections = await theSession.LoadManyAsync<DeletableProjection>(createNormal.Id, createHard.Id);
        Assert.Equal(2, createdProjections.Count);

        theSession.Events.StartStream(deleteNormal);
        theSession.Events.StartStream(deleteHard);

        await theSession.SaveChangesAsync();

        var normalDeleteInline = await theSession.LoadAsync<DeletableProjection>(createNormal.Id);
        var hardDeleteInline = await theSession.LoadAsync<DeletableProjection>(createHard.Id);
        Assert.Null(normalDeleteInline);
        Assert.Null(hardDeleteInline);

        using var daemon = await theStore.BuildProjectionDaemonAsync();

        await daemon.RebuildProjection<DeletableEventProjection>(default);

        var normalDeleteRebuilt = await theSession.LoadAsync<DeletableProjection>(createNormal.Id);
        var hardDeleteRebuilt = await theSession.LoadAsync<DeletableProjection>(createHard.Id);
        Assert.Null(normalDeleteRebuilt);
        Assert.Null(hardDeleteRebuilt);
    }

}

public record DeletableProjection(Guid Id, Guid InnerGuid);

public record DeleteEvent(Guid Id);

public record HardDeleteEvent(Guid Id);

public record CreateDeletableProjection(Guid Id, Guid InnerGuid);

public class DeletableEventProjection : EventProjection
{
    public DeletableEventProjection()
    {
        Options.DeleteViewTypeOnTeardown<DeletableProjection>();
        Project<CreateDeletableProjection>((@event, operations) =>
        {
            operations.Store(new DeletableProjection(@event.Id, @event.InnerGuid));
        });

        Project<DeleteEvent>((@event, operations) =>
        {
            operations.DeleteWhere<DeletableProjection>(x=> x.InnerGuid == @event.Id);
        });

        Project<HardDeleteEvent>((@event, operations) =>
        {
            operations.HardDeleteWhere<DeletableProjection>(x=> x.InnerGuid == @event.Id);
        });

    }
}
