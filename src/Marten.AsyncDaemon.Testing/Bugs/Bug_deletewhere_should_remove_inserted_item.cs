using System;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Metadata;
using Marten.Storage;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing.Bugs;

public class Bug_DeleteWhere_Operations_Should_Respect_Tenancy : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_DeleteWhere_Operations_Should_Respect_Tenancy(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ShouldDelete_ForDeleteCondition_AfterRebuild()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<DeletableEventProjection>(ProjectionLifecycle.Inline);
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Logger(new TestOutputMartenLogger(_output));

            opts.Advanced.DefaultTenantUsageEnabled = false;
        });

        var innerGuid = Guid.NewGuid();
        var createNormal = new CreateDeletableProjection(Guid.NewGuid(), innerGuid);
        var deleteNormal = new DeleteEvent(innerGuid);

        var innerGuid2 = Guid.NewGuid();
        var createHard = new CreateDeletableProjection(Guid.NewGuid(), innerGuid2);
        var deleteHard = new HardDeleteEvent(innerGuid2);

        await using var session = theStore.LightweightSession("test");

        session.Events.StartStream(createNormal);
        session.Events.StartStream(createHard);

        await session.SaveChangesAsync();

        var createdProjections = await session.LoadManyAsync<DeletableProjection>(createNormal.Id, createHard.Id);
        Assert.Equal(2, createdProjections.Count);

        session.Events.StartStream(deleteNormal);
        session.Events.StartStream(deleteHard);

        await session.SaveChangesAsync();

        var normalDeleteInline = await session.LoadAsync<DeletableProjection>(createNormal.Id);
        var hardDeleteInline = await session.LoadAsync<DeletableProjection>(createHard.Id);
        Assert.Null(normalDeleteInline);
        Assert.Null(hardDeleteInline);

        using var daemon = await theStore.BuildProjectionDaemonAsync();

        await daemon.RebuildProjection<DeletableEventProjection>(default);

        var normalDeleteRebuilt = await session.LoadAsync<DeletableProjection>(createNormal.Id);
        var hardDeleteRebuilt = await session.LoadAsync<DeletableProjection>(createHard.Id);
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
