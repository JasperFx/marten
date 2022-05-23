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
using Marten.Testing;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing;

public class cross_stream_aggregation: DaemonContext
{
    public cross_stream_aggregation(ITestOutputHelper output): base(output)
    {
    }

    [Fact]
    public void lifecycle_is_async_by_default()
    {
        new CrossStreamDayProjection().Lifecycle.ShouldBe(ProjectionLifecycle.Async);
    }

    [Fact]
    public async Task splicing_events()
    {
        NumberOfStreams = 10;
        await PublishMultiThreaded(3);

        var allEvents = await theSession.Events.QueryAllRawEvents().ToListAsync();

        var slicer = new CrossStreamDayProjection().As<IEventSlicer<Day, int>>();

        var slices = await slicer.SliceAsyncEvents(theSession, allEvents.ToList());

        foreach (var slice in slices.SelectMany(x => x.Slices).ToArray())
        {
            var events = slice.Events();
            events.All(x => x.Data is IDayEvent || x.Data is Movement).ShouldBeTrue();
            events.Select(x => x.Data).OfType<IDayEvent>().All(x => x.Day == slice.Id)
                .ShouldBeTrue();

            var travels = events.OfType<Event<Travel>>().ToArray();
            foreach (var travel in travels)
            {
                var index = events.As<List<IEvent>>().IndexOf(travel);

                for (var i = 0; i < travel.Data.Movements.Count; i++)
                {
                    events.ElementAt(index + i + 1).Data.ShouldBe(travel.Data.Movements[i]);
                }
            }
        }

        slices.ShouldNotBeNull();
    }

    [Fact]
    public async Task run_end_to_end()
    {
        StoreOptions(x => x.Projections.Add(new CrossStreamDayProjection()));

        await theStore.EnsureStorageExistsAsync(typeof(Day));

        using var agent = await StartDaemon();

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        _output.WriteLine($"Expecting {NumberOfEvents} events");

        await agent.Tracker.WaitForShardState("Day:All", NumberOfEvents, 2.Minutes());

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

public class CrossStreamDayProjection: CrossStreamSingleStreamAggregation<Day, int>
{
    public CrossStreamDayProjection()
    {
        ProjectionName = "Day";

        // You have to specify this in order for the Travel events
        // to flow through this projection.
        IncludeType<Travel>();
    }

    protected override ValueTask GroupEvents(IEventGrouping<int> grouping, IQuerySession session, List<IEvent> events)
    {
        // Tell the projection how to group the events
        // by Day document
        grouping.AddEventsWithMetadata<IDayEvent>(e => e.Data.Day, events);

        // This just lets the projection work independently
        // on each Movement child of the Travel event
        // as if it were its own event
        grouping.FanOutOnEach<Travel, Movement>(x => x.Movements);

        return ValueTask.CompletedTask;
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
