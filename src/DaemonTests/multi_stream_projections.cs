using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests;

public class multi_stream_projections: DaemonContext
{
    public multi_stream_projections(ITestOutputHelper output): base(output)
    {
    }

    [Fact]
    public void lifecycle_is_async_by_default()
    {
        new DayProjection().Lifecycle.ShouldBe(ProjectionLifecycle.Async);
    }

    [Fact]
    public async Task run_end_to_end()
    {
        StoreOptions(x => x.Projections.Add(new DayProjection(), ProjectionLifecycle.Async));

        await theStore.EnsureStorageExistsAsync(typeof(Day));

        using var agent = await StartDaemon();

        NumberOfStreams = 10;
        await PublishSingleThreaded();

        _output.WriteLine($"Expecting {NumberOfEvents} events");

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

            day.Stops.ShouldBe(matching
                .OfType<Travel>()
                .SelectMany(x => x.Stops)
                .Count());

            day.Version.ShouldBeGreaterThan(0);
        }
    }
}

public class Day
{
    public long Version { get; set; }

    public int Id { get; set; }

    // how many trips started on this day?
    public int Started { get; set; }

    // how many trips ended on this day?
    public int Ended { get; set; }

    public int Stops { get; set; }

    // how many miles did the active trips
    // drive in which direction on this day?
    public double North { get; set; }
    public double East { get; set; }
    public double West { get; set; }
    public double South { get; set; }
}

#region sample_showing_fanout_rules

public class DayProjection: MultiStreamProjection<Day, int>
{
    public DayProjection()
    {
        // Tell the projection how to group the events
        // by Day document
        Identity<IDayEvent>(x => x.Day);

        // This just lets the projection work independently
        // on each Movement child of the Travel event
        // as if it were its own event
        FanOut<Travel, Movement>(x => x.Movements);

        // You can also access Event data
        FanOut<Travel, Stop>(x => x.Data.Stops);

        ProjectionName = "Day";

        // Opt into 2nd level caching of up to 100
        // most recently encountered aggregates as a
        // performance optimization
        Options.CacheLimitPerTenant = 1000;

        // With large event stores of relatively small
        // event objects, moving this number up from the
        // default can greatly improve throughput and especially
        // improve projection rebuild times
        Options.BatchSize = 5000;
    }

    public void Apply(Day day, TripStarted e)
    {
        day.Started++;
    }

    public void Apply(Day day, TripEnded e)
    {
        day.Ended++;
    }

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

    public void Apply(Day day, Stop e)
    {
        day.Stops++;
    }
}

#endregion
