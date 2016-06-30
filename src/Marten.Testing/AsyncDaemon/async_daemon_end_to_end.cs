using System;
using System.Threading;
using System.Threading.Tasks;
using CodeTracker;
using Marten.Events.Projections;
using Marten.Events.Projections.Async;
using Xunit;

namespace Marten.Testing.AsyncDaemon
{
    public class async_daemon_end_to_end : IntegratedFixture, IClassFixture<AsyncDaemonFixture>
    {
        private readonly AsyncDaemonFixture _fixture;

        public async_daemon_end_to_end(AsyncDaemonFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task do_a_complete_rebuild_of_the_active_projects_from_scratch()
        {
            _fixture.PublishAllProjectEvents(theStore);

            var projection = new AggregationProjection<ActiveProject>(new AggregateFinder<ActiveProject>(), new Aggregator<ActiveProject>());
            var build = new CompleteRebuild(theStore, projection);

            var last = await build.PerformRebuild(new CancellationToken()).ConfigureAwait(false);

            Console.WriteLine(last);

            build.Dispose();

            _fixture.CompareActiveProjects(theStore);

            build.Dispose();
        }
    }
}