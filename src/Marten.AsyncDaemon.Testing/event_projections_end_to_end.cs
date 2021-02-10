using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing
{
    public class event_projections_end_to_end : DaemonContext
    {
        private readonly ITestOutputHelper _output;

        public event_projections_end_to_end(ITestOutputHelper output) : base(output)
        {
            _output = output;
        }

        [Fact]
        public void uses_event_type_filter()
        {
            var projection = new DistanceProjection();
            var filter = projection
                .AsyncProjectionShards(theStore)
                .First()
                .EventFilters
                .OfType<Marten.Events.Daemon.EventTypeFilter>()
                .Single();

            filter.EventTypes.Single().ShouldBe(typeof(Travel));
        }

        [Fact]
        public async Task run_simultaneously()
        {
            StoreOptions(x => x.Events.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async));

            NumberOfStreams = 10;

            var agent = await StartDaemon();

            var waiter = agent.Tracker.WaitForShardState("Distance:All", NumberOfEvents, 15.Seconds());

            await PublishSingleThreaded();


            await waiter;

            await CheckExpectedResults();
        }

        [Fact]
        public async Task run_simultaneously_multitenancy()
        {
            StoreOptions(x =>
            {
                x.Events.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async);
                x.Events.TenancyStyle = TenancyStyle.Conjoined;
                x.Schema.For<Distance>().MultiTenanted();
            });

            UseMixOfTenants(10);

            var agent = await StartDaemon();

            var waiter = agent.Tracker.WaitForShardState("Distance:All", NumberOfEvents, 15.Seconds());

            await PublishSingleThreaded();

            await waiter;

            await CheckExpectedResultsForTenants("a", "b");
        }

        [Fact]
        public async Task rebuild()
        {
            StoreOptions(x => x.Events.Projections.Add(new DistanceProjection(), ProjectionLifecycle.Async));

            NumberOfStreams = 10;

            var agent = await StartDaemon();

            await PublishSingleThreaded();


            await agent.RebuildProjection("Distance", CancellationToken.None);


        }

        private Task CheckExpectedResults()
        {
            return CheckExpectedResults(theSession);
        }

        private async Task CheckExpectedResultsForTenants(params string[] tenants)
        {
            foreach (var tenantId in tenants)
            {
                using (var session = theStore.LightweightSession(tenantId))
                {
                    await CheckExpectedResults(session);
                }
            }
        }



        private async Task CheckExpectedResults(IDocumentSession session)
        {
            var distances = await session.Query<Distance>().ToListAsync();

            var events = (await session.Events.QueryAllRawEvents().ToListAsync());
            var travels = events.OfType<Event<Travel>>().ToDictionary(x => x.Id);

            foreach (var distance in distances)
            {
                if (travels.TryGetValue(distance.Id, out var travel))
                {
                    distance.Day.ShouldBe(travel.Data.Day);
                    distance.Total.ShouldBe(travel.Data.TotalDistance());
                }
                else
                {
                    travel.ShouldNotBeNull();
                }

                Logger.LogDebug("Compared distance " + distance);
            }
        }
    }

    public class Distance
    {
        public Guid Id { get; set; }
        public double Total { get; set; }
        public int Day { get; set; }

        public override string ToString()
        {
            return $"{nameof(Id)}: {Id}, {nameof(Total)}: {Total}, {nameof(Day)}: {Day}";
        }
    }

    public class DistanceProjection: EventProjection
    {
        public DistanceProjection()
        {
            ProjectionName = "Distance";
        }

        public Distance Create(Travel travel, IEvent e)
        {
            return new Distance {Id = e.Id, Day = travel.Day, Total = travel.TotalDistance()};
        }
    }

    public class DistanceProjection2: SyncProjectionBase
    {
        public override void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
        {
            foreach (var @event in streams.SelectMany(x => x.Events))
            {
                switch (@event.Data)
                {
                    case IEvent<Travel> e:
                        var travel = e.GetData();
                        var distance = new Distance
                        {
                            Id = e.Id,
                            Day = travel.Day,
                            Total = travel.TotalDistance()
                        };
                        operations.Store(distance);
                        break;
                }
            }
        }
    }
}
