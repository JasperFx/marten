using System.Linq;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Progress;
using Marten.Exceptions;
using Marten.Testing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing;

public class projection_progression_operations : OneOffConfigurationsContext
{
    public projection_progression_operations()
    {
        theStore.Advanced.Clean.DeleteAllEventData();
        theStore.EnsureStorageExists(typeof(IEvent));
    }

    [Fact]
    public async Task insert_progression()
    {
        var operation1 = new InsertProjectionProgress(theStore.Events,
            new EventRange(new ShardName("one"), 12));

        var operation2 = new InsertProjectionProgress(theStore.Events,
            new EventRange( new ShardName("two"), 25));

        TheSession.QueueOperation(operation1);
        TheSession.QueueOperation(operation2);

        await TheSession.SaveChangesAsync();

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

        TheSession.QueueOperation(insertProjectionProgress);
        await TheSession.SaveChangesAsync();

        var updateProjectionProgress =
            new UpdateProjectionProgress(theStore.Events, new EventRange(new ShardName("three"), 12, 50));

        TheSession.QueueOperation(updateProjectionProgress);
        await TheSession.SaveChangesAsync();

        var progress = await theStore.Advanced.ProjectionProgressFor(new ShardName("three"));
        progress.ShouldBe(50);
    }

    [Fact]
    public async Task Bug_2201_update_successfully_but_have_deletion_next()
    {
        var target = Target.Random();
        TheSession.Store(target);
        await TheSession.SaveChangesAsync();

        var insertProjectionProgress = new InsertProjectionProgress(theStore.Events,
            new EventRange( new ShardName("three"), 12));


        TheSession.QueueOperation(insertProjectionProgress);

        await TheSession.SaveChangesAsync();

        var updateProjectionProgress =
            new UpdateProjectionProgress(theStore.Events, new EventRange(new ShardName("three"), 12, 50));

        TheSession.QueueOperation(updateProjectionProgress);
        TheSession.Delete(target);
        await TheSession.SaveChangesAsync();

        var progress = await theStore.Advanced.ProjectionProgressFor(new ShardName("three"));
        progress.ShouldBe(50);
    }

    [Fact]
    public async Task update_sad_path()
    {
        var insertProjectionProgress = new InsertProjectionProgress(theStore.Events,
            new EventRange(new ShardName("four"), 12));

        TheSession.QueueOperation(insertProjectionProgress);
        await TheSession.SaveChangesAsync();

        var updateProjectionProgress = new UpdateProjectionProgress(theStore.Events, new EventRange(new ShardName("four"), 5, 50));

        var ex = await Should.ThrowAsync<ProgressionProgressOutOfOrderException>(async () =>
        {
            TheSession.QueueOperation(updateProjectionProgress);
            await TheSession.SaveChangesAsync();
        });

        ex.Message.ShouldContain("four", StringComparisonOption.Default);

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

        TheSession.QueueOperation(operation1);
        TheSession.QueueOperation(operation2);

        await TheSession.SaveChangesAsync();

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