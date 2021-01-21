using System.Linq;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Testing.Events.Daemon.TestingSupport;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Events.Daemon
{
    public class build_aggregate_projection: DaemonContext
    {
        public build_aggregate_projection(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task end_to_end_with_events_already_published()
        {
            NumberOfStreams = 10;

            Logger.LogDebug($"The expected number of events is " + NumberOfEvents);

            await BuildAllExpectedAggregates();

            StoreOptions(x => x.Events.Projections.Async(new TripAggregation()));

            theStore.Advanced.Clean.DeleteDocumentsFor(typeof(Trip));

            var agent = await StartNodeAgent();

            await PublishSingleThreaded();


            var shard = theStore.Events.Projections.AllShards().Single();
            var waiter = agent.Tracker.WaitForShardState(new ShardState(shard, NumberOfEvents), 15.Seconds());

            await agent.StartShard(shard.ProjectionOrShardName);

            await waiter;

            await CheckAllExpectedAggregatesAgainstActuals();
        }


    }
}
