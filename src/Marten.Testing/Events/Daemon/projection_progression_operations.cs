using System.Linq;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Daemon.Progress;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Daemon
{
    public class projection_progression_operations : IntegrationContext
    {
        public projection_progression_operations(DefaultStoreFixture fixture) : base(fixture)
        {
            theStore.Advanced.Clean.DeleteAllEventData();
            theStore.Tenancy.Default.EnsureStorageExists(typeof(IEvent));
        }

        [Fact]
        public async Task insert_progression()
        {
            var operation1 = new InsertProjectionProgress(theStore.Events,
                new ProjectionProgress {ProjectionOrShardName = "one", LastSequenceId = 12});

            var operation2 = new InsertProjectionProgress(theStore.Events,
                new ProjectionProgress {ProjectionOrShardName = "two", LastSequenceId = 25});

            theSession.QueueOperation(operation1);
            theSession.QueueOperation(operation2);

            await theSession.SaveChangesAsync();

            var progress1 = await theStore.Events.ProjectionProgressFor("one");
            progress1.ShouldBe(12);

            var progress2 = await theStore.Events.ProjectionProgressFor("two");
            progress2.ShouldBe(25);
        }

        [Fact]
        public async Task update_happy_path()
        {
            var insertProjectionProgress = new InsertProjectionProgress(theStore.Events,
                new ProjectionProgress {ProjectionOrShardName = "three", LastSequenceId = 12});

            theSession.QueueOperation(insertProjectionProgress);
            await theSession.SaveChangesAsync();

            var updateProjectionProgress = new UpdateProjectionProgress(theStore.Events)
            {
                ProjectionOrShardName = "three", StartingSequence = 12, UpdatedSequence = 50
            };

            theSession.QueueOperation(updateProjectionProgress);
            await theSession.SaveChangesAsync();

            var progress = await theStore.Events.ProjectionProgressFor("three");
            progress.ShouldBe(50);
        }

        [Fact]
        public async Task update_sad_path()
        {
            var insertProjectionProgress = new InsertProjectionProgress(theStore.Events,
                new ProjectionProgress {ProjectionOrShardName = "four", LastSequenceId = 12});

            theSession.QueueOperation(insertProjectionProgress);
            await theSession.SaveChangesAsync();

            var updateProjectionProgress = new UpdateProjectionProgress(theStore.Events)
            {
                ProjectionOrShardName = "four", StartingSequence = 5, UpdatedSequence = 50
            };

            var ex = await Should.ThrowAsync<ProgressionProgressOutOfOrderException>(async () =>
            {
                theSession.QueueOperation(updateProjectionProgress);
                await theSession.SaveChangesAsync();
            });

            ex.Message.ShouldContain("four", StringComparisonOption.Default);

            // Just verifying that the real progress didn't change
            var progress = await theStore.Events.ProjectionProgressFor("four");
            progress.ShouldBe(12);
        }

        [Fact]
        public async Task fetch_all_projections()
        {
            var operation1 = new InsertProjectionProgress(theStore.Events,
                new ProjectionProgress {ProjectionOrShardName = "five", LastSequenceId = 12});

            var operation2 = new InsertProjectionProgress(theStore.Events,
                new ProjectionProgress {ProjectionOrShardName = "six", LastSequenceId = 25});

            theSession.QueueOperation(operation1);
            theSession.QueueOperation(operation2);

            await theSession.SaveChangesAsync();

            var progressions = await theStore.Events.AllProjectionProgress();

            progressions.Any(x => x.ProjectionOrShardName == "five").ShouldBeTrue();
            progressions.Any(x => x.ProjectionOrShardName == "six").ShouldBeTrue();
        }

        [Fact]
        public async Task fetch_progress_does_not_exist_returns_0()
        {
            var progress1 = await theStore.Events.ProjectionProgressFor("none");
            progress1.ShouldBe(0);
        }


    }
}
