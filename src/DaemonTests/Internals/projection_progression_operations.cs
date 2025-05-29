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
using Shouldly;
using Xunit;

namespace DaemonTests.Internals;

public class projection_progression_operations : OneOffConfigurationsContext, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
        await theStore.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
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

        await theSession.SaveChangesAsync();

        var progress1 = await theStore.Advanced.ProjectionProgressFor(new ShardName("one"));
        progress1.ShouldBe(12);

        var progress2 = await theStore.Advanced.ProjectionProgressFor(new ShardName("two"));
        progress2.ShouldBe(25);
    }

    [Fact]
    public async Task update_happy_path()
    {
        var insertProjectionProgress = new InsertProjectionProgress(theStore.Events,
            new EventRange( new ShardName("three"), 12));

        theSession.QueueOperation(insertProjectionProgress);
        await theSession.SaveChangesAsync();

        var updateProjectionProgress =
            new UpdateProjectionProgress(theStore.Events, new EventRange(new ShardName("three"), 12, 50));

        theSession.QueueOperation(updateProjectionProgress);
        await theSession.SaveChangesAsync();

        var progress = await theStore.Advanced.ProjectionProgressFor(new ShardName("three"));
        progress.ShouldBe(50);
    }

    [Fact]
    public async Task Bug_2201_update_successfully_but_have_deletion_next()
    {
        var target = Target.Random();
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        var insertProjectionProgress = new InsertProjectionProgress(theStore.Events,
            new EventRange( new ShardName("three"), 12));


        theSession.QueueOperation(insertProjectionProgress);

        await theSession.SaveChangesAsync();

        var updateProjectionProgress =
            new UpdateProjectionProgress(theStore.Events, new EventRange(new ShardName("three"), 12, 50));

        theSession.QueueOperation(updateProjectionProgress);
        theSession.Delete(target);
        await theSession.SaveChangesAsync();

        var progress = await theStore.Advanced.ProjectionProgressFor(new ShardName("three"));
        progress.ShouldBe(50);
    }

    [Fact]
    public async Task update_sad_path()
    {
        var insertProjectionProgress = new InsertProjectionProgress(theStore.Events,
            new EventRange(new ShardName("four"), 12));

        theSession.QueueOperation(insertProjectionProgress);
        await theSession.SaveChangesAsync();

        var updateProjectionProgress = new UpdateProjectionProgress(theStore.Events, new EventRange(new ShardName("four"), 5, 50));

        var ex = await Should.ThrowAsync<ProgressionProgressOutOfOrderException>(async () =>
        {
            theSession.QueueOperation(updateProjectionProgress);
            await theSession.SaveChangesAsync();
        });

        ex.Message.ShouldContain("four");

        // Just verifying that the real progress didn't change
        var progress = await theStore.Advanced.ProjectionProgressFor(new ShardName("four"));
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

        await theSession.SaveChangesAsync();

        var progressions = await theStore.Advanced.AllProjectionProgress();

        progressions.Any(x => x.ShardName == "five:All").ShouldBeTrue();
        progressions.Any(x => x.ShardName == "six:All").ShouldBeTrue();
    }

    [Fact]
    public async Task fetch_progress_does_not_exist_returns_0()
    {
        var progress1 = await theStore.Advanced.ProjectionProgressFor(new ShardName("none"));
        progress1.ShouldBe(0);
    }


}
