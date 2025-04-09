using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Testing;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests.Aggregations;

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

    [Fact]
    public async Task events_applied_in_sequence_across_streams()
    {
        StoreOptions(opts => opts.Projections.Add<Projector>(ProjectionLifecycle.Inline));

        var commonId = Guid.NewGuid();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await using var session = theStore.LightweightSession();

        session.Events.StartStream(commonId, new Happened() { Id = commonId }, new Happened() { Id = commonId });
        await session.SaveChangesAsync();

        session.Events.StartStream(new Happened() { Id = commonId }, new Happened() { Id = commonId });
        await session.SaveChangesAsync();

        var projection = await session.LoadAsync<Projection>(commonId);
        var eventSequenceList = new List<long> { 1, 2, 3, 4 };
        projection.ShouldNotBeNull();
        projection.EventSequenceList.ShouldHaveTheSameElementsAs(eventSequenceList);

        await daemon.RebuildProjectionAsync<Projector>(CancellationToken.None);
        projection = await session.LoadAsync<Projection>(commonId);
        projection.ShouldNotBeNull();
        projection.EventSequenceList.ShouldHaveTheSameElementsAs(eventSequenceList);
    }

    public interface ICommonId
    {
        public Guid Id { get; set; }
    }

    public class Happened: ICommonId
    {
        public Guid Id { get; set; }
    }

    public class Projection
    {
        public Guid Id { get; set; }
        public IList<long> EventSequenceList { get; set; } = new List<long>();
    }

    public class Projector: MultiStreamProjection<Projection, Guid>
    {
        public Projector()
        {
            Identity<ICommonId>(x => x.Id);
        }

        public void Apply(Projection p, IEvent<Happened> e) => p.EventSequenceList.Add(e.Sequence);
    }

    [Fact]
    public async Task better_is_new_logic()
    {
        Guid user1 = Guid.NewGuid();
        Guid user2 = Guid.NewGuid();
        Guid user3 = Guid.NewGuid();

        Guid issue1 = Guid.NewGuid();
        Guid issue2 = Guid.NewGuid();
        Guid issue3 = Guid.NewGuid();

        StoreOptions(opts =>
        {
            opts.Projections.AsyncMode = DaemonMode.Solo;
            opts.Projections.Add<UserIssueProjection>(ProjectionLifecycle.Async);
        });


        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(user1, new UserCreated { UserId = user1 });
            session.Events.Append(user2, new UserCreated { UserId = user2 });
            session.Events.Append(user3, new UserCreated { UserId = user3 });

            await session.SaveChangesAsync();
        }

        using var daemon = await StartDaemon();
        await daemon.StartAllAsync();

        await daemon.Tracker.WaitForShardState("UserIssue:All", 3, 15.Seconds());


        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(issue1, new IssueCreated { UserId = user1, IssueId = issue1 });
            await session.SaveChangesAsync();
        }

        // We need to ensure that the events are not processed in a single slice to hit the IsNew issue on multiple
        // slices which is what causes the loss of information in the projection.
        await daemon.Tracker.WaitForShardState("UserIssue:All", 4, 15.Seconds());

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(issue2, new IssueCreated { UserId = user1, IssueId = issue2 });
            await session.SaveChangesAsync();
        }

        await daemon.Tracker.WaitForShardState("UserIssue:All", 5, 15.Seconds());

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(issue3, new IssueCreated { UserId = user1, IssueId = issue3 });
            await session.SaveChangesAsync();
        }

        await daemon.Tracker.WaitForShardState("UserIssue:All", 6, 15.Seconds());

        await using (var session = theStore.QuerySession())
        {
            var doc = await session.LoadAsync<UserIssues>(user1);
            doc.Issues.Count.ShouldBe(3);
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


public class UserIssues
{
    [Identity] public Guid UserId { get; set; }

    public List<Issue> Issues { get; set; } = new List<Issue>();
}

public class Issue
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}

public class UserCreated: IUserEvent
{
    public Guid UserId { get; set; }
}

public class IssueCreated: IUserEvent
{
    public Guid UserId { get; set; }
    public Guid IssueId { get; set; }
    public string Name { get; set; }
}

public interface IUserEvent
{
    public Guid UserId { get; }
}

public class UserIssueProjection: MultiStreamProjection<UserIssues, Guid>
{
    public UserIssueProjection()
    {
        ProjectionName = "UserIssue";

        Identity<UserCreated>(x => x.UserId);
        Identity<IssueCreated>(x => x.UserId);
    }

    public UserIssues Create(UserCreated @event) =>
        new UserIssues { UserId = @event.UserId, Issues = new List<Issue>() };

    public void Apply(UserIssues state, IssueCreated @event) =>
        state.Issues.Add(new Issue { Id = @event.IssueId, Name = @event.Name });
}

