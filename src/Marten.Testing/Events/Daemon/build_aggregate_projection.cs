using System.Linq;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Testing.Events.Daemon.TestingSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Marten.Testing.Events.Daemon
{
    public class build_aggregate_projection: DaemonContext
    {
        [Fact]
        public async Task end_to_end()
        {
            NumberOfStreams = 10;
            await BuildAllExpectedAggregates();

            theStore.Advanced.Clean.DeleteDocumentsFor(typeof(Trip));

            var aggregation = (IProjectionSource)new TripAggregation();
            var projection = (IAsyncCapableProjection)aggregation.Build(theStore);

            var shard = projection.AsyncProjectionShards(theStore, theStore.Tenancy)
                .Single();

            var statistics = await theStore.Events.FetchStatistics();

            var agent = new ProjectionAgent(theStore, shard, new Logger<IProjection>(new NullLoggerFactory()));
            var tracker = new ShardStateTracker();
            tracker.MarkHighWater(statistics.EventSequenceNumber);

            var waiter = tracker.WaitForShardState(new ShardState(shard, statistics.EventSequenceNumber), 60.Seconds());

            await agent.Start(tracker);

            await waiter;

            await CheckAllExpectedAggregatesAgainstActuals();
        }


    }
}
