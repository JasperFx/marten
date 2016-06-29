using System.Linq;
using System.Threading.Tasks;
using CodeTracker;
using Marten.Events;
using Marten.Events.Projections.Async;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections.Async
{

    public class when_caching_pages : DaemonContext
    {
        [Fact]
        public async Task stores_the_first_page()
        {
            var thePage = new EventPage(0, 100, new EventStream[0]) { Count = 100 };

            await theDaemon.CachePage(thePage).ConfigureAwait(false);

            theDaemon.Accumulator.AllPages().Single()
                .ShouldBe(thePage);
        }

        [Fact]
        public async Task should_queue_the_page_on_each_projection_on_the_first_one()
        {
            var thePage = new EventPage(0, 100, new EventStream[0]) { Count = 100 };

            await theDaemon.CachePage(thePage).ConfigureAwait(false);


            projection.Received().QueuePage(thePage);
        }

        [Fact]
        public async Task should_not_pause_the_fetcher_if_under_the_threshold()
        {
            var thePage = new EventPage(0, 100, new EventStream[0]) { Count = 100 };

            await theDaemon.CachePage(thePage).ConfigureAwait(false);

            await theFetcher.DidNotReceive().Pause().ConfigureAwait(false);
        }

        [Fact]
        public async Task pauses_the_fetcher_if_the_queue_number_is_greater_than_the_options_threshold()
        {
            // The default for MaximumStagedEventCount is 1000
            await theDaemon.CachePage(new EventPage(0, 100, new EventStream[0]) {Count = 100}).ConfigureAwait(false);
            await theDaemon.CachePage(new EventPage(101, 200, new EventStream[0]) {Count = 100}).ConfigureAwait(false);
            await theDaemon.CachePage(new EventPage(201, 1100, new EventStream[0]) {Count = 1100 - 201}).ConfigureAwait(false);

            await theFetcher.Received().Pause().ConfigureAwait(false);
        }
    }



    public class when_stopping_the_daemon : DaemonContext
    {
        [Fact]
        public async Task should_stop_the_fetcher_and_projections()
        {
            theFetcher.Stop().Returns(Task.CompletedTask);

            projection.Stop().Returns(Task.CompletedTask);



            await theDaemon.Stop().ConfigureAwait(false);

            theFetcher.Received().Stop();

            projection.Received().Stop();
        }

    }

    public class when_storing_progress : DaemonContext
    {
        [Fact]
        public async Task store_progress_removes_obsolete_page()
        {
            var thePage = new EventPage(0, 100, new EventStream[0]) { Count = 100 };
            var thePage2 = new EventPage(101, 200, new EventStream[0]) { Count = 100 };
            await theDaemon.CachePage(thePage).ConfigureAwait(false);
            await theDaemon.CachePage(thePage2).ConfigureAwait(false);

            projection.LastEncountered.Returns(100);

            await theDaemon.StoreProgress(typeof(ActiveProject), thePage).ConfigureAwait(false);

            theDaemon.Accumulator.AllPages().Single()
                .ShouldBe(thePage2);
        }

        public async Task should_restart_the_fetcher_if_it_was_paused_and_below_the_threshold()
        {
            theFetcher.State.Returns(FetcherState.Paused);

            var thePage = new EventPage(0, 100, new EventStream[0]) { Count = 100 };
            await theDaemon.CachePage(thePage).ConfigureAwait(false);

            projection.LastEncountered.Returns(100);

            await theDaemon.StoreProgress(typeof(ActiveProject), thePage).ConfigureAwait(false);

            theFetcher.Received().Start(theDaemon, true);
        }
    }


    public class when_starting_the_daemon : DaemonContext
    {
        public when_starting_the_daemon()
        {
            theDaemon.Start();
        }

        [Fact]
        public void should_start_the_projections_with_the_update_block()
        {
            projection.Received().Updater = theDaemon.UpdateBlock;

        }

        [Fact]
        public void should_start_the_fetcher_with_auto_restart()
        {
            theFetcher.Received().Start(theDaemon, true);
        }
    }

    public abstract class DaemonContext
    {
        protected readonly IFetcher theFetcher = Substitute.For<IFetcher>();
        protected Daemon theDaemon;
        protected IProjectionTrack projection;

        protected DaemonOptions theOptions = new DaemonOptions(new EventGraph(new StoreOptions()));

        public DaemonContext()
        {
            projection = Substitute.For<IProjectionTrack>();

            theDaemon = new Daemon(theOptions, theFetcher, projection);



        }


    }
}