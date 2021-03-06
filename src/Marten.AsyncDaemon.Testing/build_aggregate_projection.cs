using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing
{
    public class build_aggregate_projection: DaemonContext
    {
        public build_aggregate_projection(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void uses_event_type_filter_for_base_filter_when_not_using_base_types()
        {
            var projection = new TripAggregation();
            var filter = projection
                .AsyncProjectionShards(theStore)
                .First()
                .EventFilters
                .OfType<EventTypeFilter>()
                .Single();

            filter.EventTypes.ShouldContain(typeof(TripAborted));
            filter.EventTypes.ShouldContain(typeof(Arrival));
            filter.EventTypes.ShouldContain(typeof(Travel));
            filter.EventTypes.ShouldContain(typeof(TripEnded));
            filter.EventTypes.ShouldContain(typeof(TripStarted));
        }

        [Fact]
        public async Task end_to_end_with_events_already_published()
        {
            NumberOfStreams = 10;

            Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

            StoreOptions(x =>
            {
                x.Events.Projections.Add(new TripAggregation(), ProjectionLifecycle.Async);
                x.Logger(new TestOutputMartenLogger(_output));
            }, true);

            var agent = await StartDaemon();

            await PublishSingleThreaded();


            var shard = theStore.Events.Projections.AllShards().Single();
            var waiter = agent.Tracker.WaitForShardState(new ShardState(shard, NumberOfEvents), 15.Seconds());

            await agent.StartShard(shard.Name.Identity, CancellationToken.None);

            await waiter;

            await CheckAllExpectedAggregatesAgainstActuals();
        }

        [Fact]
        public async Task build_with_multi_tenancy()
        {
            StoreOptions(x =>
            {
                x.Events.TenancyStyle = TenancyStyle.Conjoined;
                x.Events.Projections.Add(new TripAggregation(), ProjectionLifecycle.Async);
                x.Schema.For<Trip>().MultiTenanted();
            }, true);

            UseMixOfTenants(10);

            Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

            var agent = await StartDaemon();

            var shard = theStore.Events.Projections.AllShards().Single();
            var waiter = agent.Tracker.WaitForShardState(new ShardState(shard, NumberOfEvents), 60.Seconds());

            await PublishSingleThreaded();

            await waiter;

            await CheckAllExpectedAggregatesAgainstActuals("a");
            await CheckAllExpectedAggregatesAgainstActuals("b");

        }

        [Fact]
        public async Task rebuild_the_projection()
        {
            NumberOfStreams = 10;

            Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

            StoreOptions(x => x.Events.Projections.Add(new TripAggregation(), ProjectionLifecycle.Async), true);

            var agent = await StartDaemon();

            await PublishSingleThreaded();

            var waiter = agent.Tracker.WaitForShardState(new ShardState("Trip:All", NumberOfEvents), 30.Seconds());

            await waiter;
            Logger.LogDebug("About to rebuild Trip:All");
            await agent.RebuildProjection("Trip", CancellationToken.None);
            Logger.LogDebug("Done rebuilding Trip:All");
            await CheckAllExpectedAggregatesAgainstActuals();
        }

        [Fact]
        public async Task rebuild_the_projection_without_custom_name()
        {
            NumberOfStreams = 10;

            Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

            StoreOptions(x => x.Events.Projections.Add<TripAggregationWithoutCustomName>(ProjectionLifecycle.Async), true);

            var agent = await StartDaemon();

            await PublishSingleThreaded();

            var waiter = agent.Tracker.WaitForShardState(new ShardState("Trip:All", NumberOfEvents), 30.Seconds());

            await waiter;
            Logger.LogDebug("About to rebuild Trip:All");
            await agent.RebuildProjection<Trip>(CancellationToken.None);
            Logger.LogDebug("Done rebuilding Trip:All");
            await CheckAllExpectedAggregatesAgainstActuals();
        }
    }
}
