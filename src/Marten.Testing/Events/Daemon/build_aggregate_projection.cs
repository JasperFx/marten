using System.Linq;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Testing.Events.Daemon.TestingSupport;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Events.Daemon
{
    public class build_aggregate_projection: DaemonContext
    {
        private readonly ITestOutputHelper _output;

        public build_aggregate_projection(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task end_to_end_with_events_already_published()
        {
            NumberOfStreams = 10;

            _output.WriteLine($"The expected number of events is " + NumberOfEvents);

            await BuildAllExpectedAggregates();

            StoreOptions(x => x.Events.Projections.Async(new TripAggregation()));

            theStore.Advanced.Clean.DeleteDocumentsFor(typeof(Trip));

            var logger = new TestLogger<IProjection>(_output);
            var agent = new NodeAgent(theStore, logger);

            agent.Start();


            await PublishSingleThreaded();

            await agent.StartAll();

            var shard = theStore.Events.Projections.AllShards().Single();
            var waiter = agent.Tracker.WaitForShardState(new ShardState(shard, NumberOfEvents), 15.Seconds());

            await agent.StartShard(shard.ProjectionOrShardName);

            await waiter;

            await CheckAllExpectedAggregatesAgainstActuals();
        }


    }
}
