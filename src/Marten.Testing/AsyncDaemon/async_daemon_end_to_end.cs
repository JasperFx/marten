using System.Threading;
using System.Threading.Tasks;
using CodeTracker;
using Marten.Events.Projections.Async;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.AsyncDaemon
{
    public class async_daemon_end_to_end : IntegratedFixture, IClassFixture<AsyncDaemonFixture>
    {
        
        public async_daemon_end_to_end(AsyncDaemonFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _logger = new TracingLogger(output.WriteLine);
        }
        
//        public async_daemon_end_to_end()
//        {
//            _fixture = new AsyncDaemonFixture();
//            _logger = new ConsoleDaemonLogger();
//        }

        private readonly AsyncDaemonFixture _fixture;
        private readonly IDaemonLogger _logger;

        [Fact] // - NOT PASSING CONSISTENTLY. #sadtrombone
        public async Task build_continuously_as_events_flow_in()
        {
            StoreOptions(_ => { _.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>(); });

            using (var daemon = theStore.BuildProjectionDaemon(logger:_logger))
            {
                daemon.StartAll();

                await _fixture.PublishAllProjectEventsAsync(theStore);

                // Runs all projections until there are no more events coming in
                await daemon.WaitForNonStaleResults().ConfigureAwait(false);

                await daemon.StopAll().ConfigureAwait(false);
            }


            _fixture.CompareActiveProjects(theStore);
        }

        [Fact]
        public async Task do_a_complete_rebuild_of_the_active_projects_from_scratch()
        {
            StoreOptions(_ => { _.Events.AsyncProjections.AggregateStreamsWith<ActiveProject>(); });

            _fixture.PublishAllProjectEvents(theStore);


            using (var daemon = theStore.BuildProjectionDaemon(logger: _logger))
            {
                await daemon.Rebuild<ActiveProject>().ConfigureAwait(false);
            }

            _fixture.CompareActiveProjects(theStore);
        }
    }
}