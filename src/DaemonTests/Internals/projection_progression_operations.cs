using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon.Progress;
using Marten.Exceptions;
using Marten.Testing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DaemonTests.Internals;

public class projection_progression_operations : OneOffConfigurationsContext, IAsyncLifetime
{
    public override async ValueTask InitializeAsync()
    {
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
        await theStore.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public override ValueTask DisposeAsync()
    {
        Dispose();
        return base.DisposeAsync();
    }

    [Fact]
    public async Task insert_progression()
    {
        var operation1 = new InsertProjectionProgress(theStore.Events,
            new EventRange(new ShardName("one"), 12));

        var operation2 = new InsertProjectionProgress(theStore.Events,
            new EventRange( new ShardName("two"), 25));

        theSession.QueueOperation(operation1);
        theSession.QueueOperation(operation2);

        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        var progress1 = await theStore.Advanced.ProjectionProgressFor(new ShardName("one"), token: TestContext.Current.CancellationToken);
        progress1.ShouldBe(12);

        var progress2 = await theStore.Advanced.ProjectionProgressFor(new ShardName("two"), token: TestContext.Current.CancellationToken);
        progress2.ShouldBe(25);
    }

    [Fact]
    public async Task update_happy_path()
    {
        var insertProjectionProgress = new InsertProjectionProgress(theStore.Events,
            new EventRange( new ShardName("three"), 12));

        theSession.QueueOperation(insertProjectionProgress);
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        var updateProjectionProgress =
            new UpdateProjectionProgress(theStore.Events, new EventRange(new ShardName("three"), 12, 50, Substitute.For<ISubscriptionAgent>()));

        theSession.QueueOperation(updateProjectionProgress);
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        var progress = await theStore.Advanced.ProjectionProgressFor(new ShardName("three"), token: TestContext.Current.CancellationToken);
        progress.ShouldBe(50);
    }

    [Fact]
    public async Task Bug_2201_update_successfully_but_have_deletion_next()
    {
        var target = Target.Random();
        theSession.Store(target);
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        var insertProjectionProgress = new InsertProjectionProgress(theStore.Events,
            new EventRange( new ShardName("three"), 12));


        theSession.QueueOperation(insertProjectionProgress);

        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        var updateProjectionProgress =
            new UpdateProjectionProgress(theStore.Events, new EventRange(new ShardName("three"), 12, 50, Substitute.For<ISubscriptionAgent>()));

        theSession.QueueOperation(updateProjectionProgress);
        theSession.Delete(target);
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        var progress = await theStore.Advanced.ProjectionProgressFor(new ShardName("three"), token: TestContext.Current.CancellationToken);
        progress.ShouldBe(50);
    }

    [Fact]
    public async Task update_sad_path()
    {
        var insertProjectionProgress = new InsertProjectionProgress(theStore.Events,
            new EventRange(new ShardName("four"), 12));

        theSession.QueueOperation(insertProjectionProgress);
        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        var updateProjectionProgress = new UpdateProjectionProgress(theStore.Events, new EventRange(new ShardName("four"), 5, 50, Substitute.For<ISubscriptionAgent>()));

        var ex = await Should.ThrowAsync<ProgressionProgressOutOfOrderException>(async () =>
        {
            theSession.QueueOperation(updateProjectionProgress);
            await theSession.SaveChangesAsync();
        });

        ex.Message.ShouldContain("four");

        // Just verifying that the real progress didn't change
        var progress = await theStore.Advanced.ProjectionProgressFor(new ShardName("four"), token: TestContext.Current.CancellationToken);
        progress.ShouldBe(12);
    }

    [Fact]
    public async Task fetch_all_projections()
    {
        var operation1 = new InsertProjectionProgress(theStore.Events,
            new EventRange(new ShardName("five"), 12));

        var operation2 = new InsertProjectionProgress(theStore.Events,
            new EventRange(new ShardName("six"), 25));

        theSession.QueueOperation(operation1);
        theSession.QueueOperation(operation2);

        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        var progressions = await theStore.Advanced.AllProjectionProgress(token: TestContext.Current.CancellationToken);

        progressions.Any(x => x.ShardName == "five:All").ShouldBeTrue();
        progressions.Any(x => x.ShardName == "six:All").ShouldBeTrue();
    }

    [Fact]
    public async Task fetch_progress_does_not_exist_returns_0()
    {
        var progress1 = await theStore.Advanced.ProjectionProgressFor(new ShardName("none"), token: TestContext.Current.CancellationToken);
        progress1.ShouldBe(0);
    }


}
