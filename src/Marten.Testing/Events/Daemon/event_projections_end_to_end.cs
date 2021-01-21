using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Testing.Events.Daemon.TestingSupport;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Shouldly;

namespace Marten.Testing.Events.Daemon
{
    public class event_projections_end_to_end : DaemonContext
    {
        private readonly ITestOutputHelper _output;

        public event_projections_end_to_end(ITestOutputHelper output) : base(output)
        {
            _output = output;
        }

        [Fact]
        public async Task run_simultaneously()
        {
            StoreOptions(x => x.Events.Projections.Async(new DistanceProjection()));

            NumberOfStreams = 10;

            var agent = await StartNodeAgent();

            var waiter = agent.Tracker.WaitForShardState("Distance", NumberOfEvents, 15.Seconds());

            await PublishSingleThreaded();


            await waiter;

            var distances = await theSession.Query<Distance>().ToListAsync();

            var events = (await theSession.Events.QueryAllRawEvents().ToListAsync());
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

        public Distance Create(Event<Travel> travel)
        {
            return new Distance {Id = travel.Id, Day = travel.Data.Day, Total = travel.Data.TotalDistance()};
        }
    }
}
