using System.Linq;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Events.Projections.Async;
using Marten.Storage;
using Marten.Testing.CodeTracker;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections.Async
{
    public class when_caching_pages: ProjectionTrackContext
    {
        [Fact]
        public async Task stores_the_first_page()
        {
            var thePage = new EventPage(0, 100, EventMother.Random(100));

            await theProjectionTrack.CachePage(thePage).ConfigureAwait(false);

            theProjectionTrack.Accumulator.AllPages().Single()
                .ShouldBe(thePage);
        }

        [Fact]
        public async Task should_queue_the_page_on_each_projection_on_the_first_one()
        {
            var thePage = new EventPage(0, 100, EventMother.Random(100));

            await theProjectionTrack.CachePage(thePage).ConfigureAwait(false);
        }

        [Fact]
        public async Task should_not_pause_the_fetcher_if_under_the_threshold()
        {
            var thePage = new EventPage(0, 100, EventMother.Random(100));

            await theProjectionTrack.CachePage(thePage).ConfigureAwait(false);

            await theFetcher.DidNotReceive().Pause().ConfigureAwait(false);
        }

        [Fact]
        public async Task pauses_the_fetcher_if_the_queue_number_is_greater_than_the_options_threshold()
        {
            // The default for MaximumStagedEventCount is 1000
            await theProjectionTrack.CachePage(new EventPage(0, 100, EventMother.Random(100))).ConfigureAwait(false);
            await theProjectionTrack.CachePage(new EventPage(101, 200, EventMother.Random(100))).ConfigureAwait(false);
            await theProjectionTrack.CachePage(new EventPage(201, 1100, EventMother.Random(1001100 - 201))).ConfigureAwait(false);

            await theFetcher.Received().Pause().ConfigureAwait(false);
        }
    }

    public class when_stopping_a_projection_track: ProjectionTrackContext
    {
        [Fact]
        public async Task should_stop_the_fetcher_and_projections()
        {
            theFetcher.Stop().Returns(Task.CompletedTask);

            await theProjectionTrack.Stop().ConfigureAwait(false);

            await theFetcher.Received().Stop();
        }
    }

    public class when_storing_progress: ProjectionTrackContext
    {
        [Fact]
        public async Task store_progress_removes_obsolete_page()
        {
            var thePage = new EventPage(0, 100, EventMother.Random(100));
            var thePage2 = new EventPage(101, 200, EventMother.Random(100));
            await theProjectionTrack.CachePage(thePage).ConfigureAwait(false);
            await theProjectionTrack.CachePage(thePage2).ConfigureAwait(false);

            await theProjectionTrack.StoreProgress(typeof(ActiveProject), thePage).ConfigureAwait(false);

            theProjectionTrack.Accumulator.AllPages().Single()
                .ShouldBe(thePage2);
        }

        [Fact]
        public async Task should_restart_the_fetcher_if_it_was_paused_and_below_the_threshold()
        {
            theFetcher.State.Returns(FetcherState.Paused);

            var thePage = new EventPage(0, 100, EventMother.Random(100));
            await theProjectionTrack.CachePage(thePage).ConfigureAwait(false);

            await theProjectionTrack.StoreProgress(typeof(ActiveProject), thePage).ConfigureAwait(false);

            theFetcher.Received().Start(theProjectionTrack, theProjectionTrack.Lifecycle);
        }
    }

    public abstract class ProjectionTrackContext
    {
        protected readonly IFetcher theFetcher = Substitute.For<IFetcher>();
        protected ProjectionTrack theProjectionTrack;
        protected IProjection projection;

        public ProjectionTrackContext()
        {
            projection = Substitute.For<IProjection>();

            projection.AsyncOptions.Returns(new AsyncOptions());

            theProjectionTrack = new ProjectionTrack(theFetcher, TestingDocumentStore.Basic(), projection, Substitute.For<IDaemonLogger>(), new StubErrorHandler(), Substitute.For<ITenant>());
        }
    }
}
