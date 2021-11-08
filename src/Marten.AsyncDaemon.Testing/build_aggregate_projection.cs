using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Aggregation;
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
            var projection = new TripAggregationWithCustomName();
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
                x.Projections.Add(new TripAggregationWithCustomName(), ProjectionLifecycle.Async);
                x.Logger(new TestOutputMartenLogger(_output));
            }, true);

            var agent = await StartDaemon();

            await PublishSingleThreaded();


            var shard = theStore.Options.Projections.AllShards().Single();
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
                x.Projections.Add(new TripAggregationWithCustomName(), ProjectionLifecycle.Async);
                x.Schema.For<Trip>().MultiTenanted();
            }, true);

            UseMixOfTenants(5);

            Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

            var agent = await StartDaemon();

            var shard = theStore.Options.Projections.AllShards().Single();
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

            StoreOptions(x => x.Projections.Add(new TripAggregationWithCustomName(), ProjectionLifecycle.Async), true);

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

            StoreOptions(x => x.Projections.Add<TestingSupport.TripProjection>(ProjectionLifecycle.Async), true);

            var agent = await StartDaemon();

            await PublishSingleThreaded();

            var waiter = agent.Tracker.WaitForShardState(new ShardState("Trip:All", NumberOfEvents), 30.Seconds());

            await waiter;
            Logger.LogDebug("About to rebuild Trip:All");
            await agent.RebuildProjection<Trip>(CancellationToken.None);
            Logger.LogDebug("Done rebuilding Trip:All");
            await CheckAllExpectedAggregatesAgainstActuals();
        }

        [Fact]
        public async Task delete_when_delete_event_happens()
        {
            NumberOfStreams = 10;

            Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

            StoreOptions(x => x.Projections.Add<TestingSupport.TripProjection>(ProjectionLifecycle.Async), true);

            var agent = await StartDaemon();

            await PublishSingleThreaded();

            var waiter = agent.Tracker.WaitForShardState(new ShardState("Trip:All", NumberOfEvents), 30.Seconds());

            await waiter;

            foreach (var stream in Streams)
            {
                if (stream.Events.OfType<TripAborted>().Any())
                {
                    (await theSession.LoadAsync<Trip>(stream.StreamId)).ShouldBeNull();
                }
                else
                {
                    (await theSession.LoadAsync<Trip>(stream.StreamId)).ShouldNotBeNull();
                }
            }
        }

        [Fact]
        public async Task conditional_deletes_through_lambda_conditions_on_event_only()
        {
            NumberOfStreams = 2;

            Logger.LogDebug("The expected number of events is {NumberOfEvents}", NumberOfEvents);

            var projection = new TestingSupport.TripProjection();
            projection.ProjectionName = "Trip";

            StoreOptions(x => x.Projections.Add(projection, ProjectionLifecycle.Async), true);

            var agent = await StartDaemon();

            await PublishSingleThreaded();

            var waiter = agent.Tracker.WaitForShardState(new ShardState("Trip:All", NumberOfEvents), 30.Seconds());

            await waiter;

            var days = await theSession.Query<Trip>().ToListAsync();

            var notCriticalBreakdownStream = days[0].Id;
            var criticalBreakdownStream = days[1].Id;

            theSession.Events.Append(notCriticalBreakdownStream, new Breakdown {IsCritical = false});
            theSession.Events.Append(criticalBreakdownStream, new Breakdown {IsCritical = true});

            await theSession.SaveChangesAsync();

            var waiter2 = agent.Tracker.WaitForShardState(new ShardState("Trip:All", NumberOfEvents + 2), 300.Seconds());

            await waiter2;



            using var query = theStore.QuerySession();

            (await query.LoadAsync<Trip>(notCriticalBreakdownStream)).ShouldNotBeNull();
            (await query.LoadAsync<Trip>(criticalBreakdownStream)).ShouldBeNull();


        }


        [Fact]
        public async Task conditional_deletes_through_lambda_conditions_on_aggregate()
        {

            var shortTrip = new TripStream().TravelIsUnder(200);
            var longTrip = new TripStream().TravelIsOver(2000);
            var initialCount = shortTrip.Events.Count + longTrip.Events.Count;

            _output.WriteLine($"Initially publishing {initialCount} events");

            var projection = new TestingSupport.TripProjection();
            projection.ProjectionName = "Trip";

            StoreOptions(x => x.Projections.Add(projection, ProjectionLifecycle.Async), true);

            var agent = await StartDaemon();



            var waiter1 = agent.Tracker.WaitForShardState("Trip:All", initialCount);

            using (var session = theStore.LightweightSession())
            {
                session.Events.Append(shortTrip.StreamId, shortTrip.Events.ToArray());
                session.Events.Append(longTrip.StreamId, longTrip.Events.ToArray());
                await session.SaveChangesAsync();
            }

            await waiter1;

            // This should not trigger a delete
            theSession.Events.Append(shortTrip.StreamId, new VacationOver());

            // This should trigger a delete
            theSession.Events.Append(longTrip.StreamId, new VacationOver());

            await theSession.SaveChangesAsync();

            var totalNumberOfEvents = initialCount + 2;
            var waiter2 = agent.Tracker.WaitForShardState(new ShardState("Trip:All", totalNumberOfEvents), 30.Seconds());

            await waiter2;



            using var query = theStore.QuerySession();

            (await query.LoadAsync<Trip>(shortTrip.StreamId)).ShouldNotBeNull();
            (await query.LoadAsync<Trip>(longTrip.StreamId)).ShouldBeNull();


        }

        [Fact]
        public async Task rebuild_with_interface_creation()
        {
            StoreOptions(x =>
            {
                x.Events.TenancyStyle = TenancyStyle.Conjoined;
                x.Policies.AllDocumentsAreMultiTenanted();
                x.Projections.Add(new InterfaceCreationProjection());
            }, true);

            var id = Guid.NewGuid();
            await using (var session = theStore.LightweightSession("a"))
            {
                session.Events.StartStream(id, new FooCreated(id, "Foo"));
                session.Events.StartStream(Guid.NewGuid(), new BarCreated(Guid.NewGuid()));

                await session.SaveChangesAsync();

                var foo = await session.LoadAsync<Foo>(id);
                Assert.Equal("Foo", foo.Name);
            }

            var daemon = theStore.BuildProjectionDaemon();

            await daemon.RebuildProjection("Foo", CancellationToken.None);

            await using var session2 = theStore.LightweightSession("a");
            var c = await session2.LoadAsync<Foo>(id);
            Assert.Equal("Foo", c.Name);
        }


        public class InterfaceCreationProjection: AggregateProjection<Foo>
        {
            public InterfaceCreationProjection()
            {
                ProjectionName = nameof(Foo);
                Lifecycle = ProjectionLifecycle.Inline;

                CreateEvent<IFooCreated>(e => new(e.Id, "Foo"));
            }
        }

        public interface IFooCreated { Guid Id { get; init; } }
        public record BarCreated(Guid Id);
        public record FooCreated(Guid Id, string Name): IFooCreated;
        public record Foo(Guid Id, string Name);

        [Fact]
        public async Task rebuild_with_abstract_creation()
        {
            StoreOptions(x =>
            {
                x.Events.TenancyStyle = TenancyStyle.Conjoined;
                x.Policies.AllDocumentsAreMultiTenanted();
                x.Projections.Add(new AbstractCreationProjection());
            }, true);

            var id = Guid.NewGuid();
            await using (var session = theStore.LightweightSession("a"))
            {
                session.Events.StartStream(id, new FooCreated2(id, "Foo"));
                session.Events.StartStream(Guid.NewGuid(), new BarCreated(Guid.NewGuid()));

                await session.SaveChangesAsync();

                var foo = await session.LoadAsync<Foo>(id);
                Assert.Equal("Foo", foo.Name);
            }

            var daemon = theStore.BuildProjectionDaemon();

            await daemon.RebuildProjection("Foo", CancellationToken.None);

            await using var session2 = theStore.LightweightSession("a");
            var c = await session2.LoadAsync<Foo>(id);
            Assert.Equal("Foo", c.Name);
        }


        public class AbstractCreationProjection: AggregateProjection<Foo>
        {
            public AbstractCreationProjection()
            {
                ProjectionName = nameof(Foo);
                Lifecycle = ProjectionLifecycle.Inline;

                CreateEvent<AbstractFooCreated>(e => new(e.Id, "Foo"));
            }
        }

        public abstract record AbstractFooCreated(Guid Id);
        public record FooCreated2(Guid Id, string Name): AbstractFooCreated(Id);

    }
}
