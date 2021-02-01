using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Baseline.Dates;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing
{
    public class ViewProjectionTests : DaemonContext
    {
        public ViewProjectionTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void lifecycle_is_async_by_default()
        {
            new DayProjection().Lifecycle.ShouldBe(ProjectionLifecycle.Async);
        }

        [Fact]
        public async Task splicing_events()
        {
            NumberOfStreams = 10;
            await PublishMultiThreaded(3);

            var allEvents = await theSession.Events.QueryAllRawEvents().ToListAsync();

            var projection = (IEventSlicer<Day, int>)new DayProjection();

            var slices = projection.Slice(allEvents, theStore.Tenancy)
                .SelectMany(x => x.Slices).ToArray();

            foreach (var slice in slices)
            {
                slice.Events.All(x => x.Data is IDayEvent || x.Data is Movement).ShouldBeTrue();
                slice.Events.Select(x => x.Data).OfType<IDayEvent>().All(x => x.Day == slice.Id)
                    .ShouldBeTrue();

                var travels = slice.Events.OfType<Event<Travel>>().ToArray();
                foreach (var travel in travels)
                {
                    var index = slice.Events.As<List<IEvent>>().IndexOf(travel);

                    for (var i = 0; i < travel.Data.Movements.Count; i++)
                    {
                        slice.Events.ElementAt(index + i + 1).Data.ShouldBeTheSameAs(travel.Data.Movements[i]);
                    }
                }
            }

            slices.ShouldNotBeNull();
        }

        [Fact]
        public async Task run_end_to_end()
        {


            StoreOptions(x => x.Events.Projections.Add(new DayProjection()));

            theStore.Tenancy.Default.EnsureStorageExists(typeof(Day));

            using var agent = await StartDaemon();

            NumberOfStreams = 10;
            await PublishSingleThreaded();

            await agent.Tracker.WaitForShardState("Day:All", NumberOfEvents, 30.Seconds());

            var days = await theSession.Query<Day>().ToListAsync();

            var allEvents = await theSession.Events.QueryAllRawEvents().ToListAsync();
            var dayEvents = allEvents.Select(x => x.Data).OfType<IDayEvent>();
            var groups = dayEvents.GroupBy(x => x.Day).ToList();

            foreach (var day in days)
            {
                var matching = groups.FirstOrDefault(x => x.Key == day.Id);
                matching.ShouldNotBeNull();

                day.Started.ShouldBe(matching.OfType<TripStarted>().Count());
                day.Ended.ShouldBe(matching.OfType<TripEnded>().Count());
                day.East.ShouldBe(matching
                    .OfType<Travel>()
                    .SelectMany(x => x.Movements)
                    .Where(x => x.Direction == Direction.East)
                    .Sum(x => x.Distance));
            }
        }


    }

    public class Day
    {
        public int Id { get; set; }
        public int Started { get; set; }
        public int Ended { get; set; }
        public double North { get; set; }
        public double East { get; set; }
        public double West { get; set; }
        public double South { get; set; }
    }

    public class DayProjection: ViewProjection<Day, int>
    {
        public DayProjection()
        {
            Identity<IDayEvent>(x => x.Day);
            FanOut<Travel, Movement>(x => x.Movements);
            ProjectionName = "Day";
        }

        public void Apply(Day day, TripStarted e) => day.Started++;
        public void Apply(Day day, TripEnded e) => day.Ended++;

        public void Apply(Day day, Movement e)
        {
            switch (e.Direction)
            {
                case Direction.East:
                    day.East += e.Distance;
                    break;
                case Direction.North:
                    day.North += e.Distance;
                    break;
                case Direction.South:
                    day.South += e.Distance;
                    break;
                case Direction.West:
                    day.West += e.Distance;
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
