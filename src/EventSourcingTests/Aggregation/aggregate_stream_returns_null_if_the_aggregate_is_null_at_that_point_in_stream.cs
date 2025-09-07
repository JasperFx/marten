using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class aggregate_stream_returns_null_if_the_aggregate_is_null_at_that_point_in_stream: OneOffConfigurationsContext
{

    [Fact]
    public async Task return_null_when_the_stream_does_not_exist_as_guid_identity()
    {
        StoreOptions(opts => opts.Projections.Add<HardDeletedStartAndStopProjection>(ProjectionLifecycle.Live));
        var aggregate = await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate>(Guid.NewGuid());
        aggregate.ShouldBeNull();
    }

    [Fact]
    public async Task return_correct_data_at_version_as_guid_identity()
    {
        StoreOptions(opts => opts.Projections.Add<HardDeletedStartAndStopProjection>(ProjectionLifecycle.Live));
        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<HardDeletedStartAndStopAggregate>(streamId,
            // 1
            new Start(),
            // 2
            new Increment(),
            // 3
            new Increment(),
            // 4
            new End(),

            // 5
            new Restart(),

            // 6
            new Increment());

        await theSession.SaveChangesAsync();

        // Final state
        (await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate>(streamId)).Count.ShouldBe(1);

        // Version 1
        (await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate>(streamId, 1)).Count.ShouldBe(0);

        // Version 2 & 3
        (await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate>(streamId, 2)).Count.ShouldBe(1);
        (await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate>(streamId, 3)).Count.ShouldBe(2);

        // Version 4 should be null!
        (await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate>(streamId, 4)).ShouldBeNull();

        // Version 5 restarts back at 0
        (await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate>(streamId, 5)).Count.ShouldBe(0);

        // Version 6 increments one again
        (await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate>(streamId, 6)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task return_null_when_the_stream_does_not_exist_as_string_identity()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<HardDeletedStartAndStopProjection2>(ProjectionLifecycle.Live);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var aggregate = await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate2>(Guid.NewGuid().ToString());
        aggregate.ShouldBeNull();
    }

    [Fact]
    public async Task return_correct_data_at_version_as_string_identity()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<HardDeletedStartAndStopProjection2>(ProjectionLifecycle.Live);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        });

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.StartStream<HardDeletedStartAndStopAggregate2>(streamId,
            // 1
            new Start(),
            // 2
            new Increment(),
            // 3
            new Increment(),
            // 4
            new End(),

            // 5
            new Restart(),

            // 6
            new Increment());

        await theSession.SaveChangesAsync();

        // Final state
        (await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate2>(streamId)).Count.ShouldBe(1);

        // Version 1
        (await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate2>(streamId, 1)).Count.ShouldBe(0);

        // Version 2 & 3
        (await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate2>(streamId, 2)).Count.ShouldBe(1);
        (await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate2>(streamId, 3)).Count.ShouldBe(2);

        // Version 4 should be null!
        (await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate2>(streamId, 4)).ShouldBeNull();

        // Version 5 restarts back at 0
        (await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate2>(streamId, 5)).Count.ShouldBe(0);

        // Version 6 increments one again
        (await theSession.Events.AggregateStreamAsync<HardDeletedStartAndStopAggregate2>(streamId, 6)).Count.ShouldBe(1);
    }
}

public class HardDeletedStartAndStopAggregate
{
    public int Count { get; set; }

    public Guid Id { get; set; }

    public void Increment()
    {
        Count++;
    }
}

public class HardDeletedStartAndStopProjection: SingleStreamProjection<HardDeletedStartAndStopAggregate, Guid>
{
    public HardDeletedStartAndStopProjection()
    {
        // This is an optional, but potentially important optimization
        // for the async daemon so that it sets up an allow list
        // of the event types that will be run through this projection
        IncludeType<Start>();
        IncludeType<End>();
        IncludeType<Restart>();
        IncludeType<Increment>();
    }

    public override (HardDeletedStartAndStopAggregate?, ActionType) DetermineAction(HardDeletedStartAndStopAggregate? snapshot, Guid identity,
        IReadOnlyList<IEvent> events)
    {
        var actionType = ActionType.Store;

        if (snapshot == null && events.HasNoEventsOfType<Start>())
        {
            return (snapshot, ActionType.Nothing);
        }

        var eventData = events.ToQueueOfEventData();
        while (eventData.Any())
        {
            var data = eventData.Dequeue();
            switch (data)
            {
                case Start:
                    snapshot = new HardDeletedStartAndStopAggregate
                    {
                        // Have to assign the identity ourselves
                        Id = identity
                    };
                    break;

                case Increment when snapshot is { }:
                    // Use explicit code to only apply this event
                    // if the snapshot already exists
                    snapshot.Increment();
                    break;

                case End when snapshot is {}:
                    actionType = ActionType.HardDelete;
                    snapshot = null;
                    break;

                case Restart when snapshot == null:
                    // Got to "undo" the soft delete status
                    actionType = ActionType.Store;
                    snapshot = new HardDeletedStartAndStopAggregate { Id = identity };
                    break;
            }
        }

        return (snapshot, actionType);
    }

}

public class HardDeletedStartAndStopAggregate2
{
    public int Count { get; set; }

    public string Id { get; set; }

    public void Increment()
    {
        Count++;
    }
}

public class HardDeletedStartAndStopProjection2: SingleStreamProjection<HardDeletedStartAndStopAggregate2, string>
{
    public HardDeletedStartAndStopProjection2()
    {
        // This is an optional, but potentially important optimization
        // for the async daemon so that it sets up an allow list
        // of the event types that will be run through this projection
        IncludeType<Start>();
        IncludeType<End>();
        IncludeType<Restart>();
        IncludeType<Increment>();
    }

    public override (HardDeletedStartAndStopAggregate2, ActionType) DetermineAction(HardDeletedStartAndStopAggregate2 snapshot,
        string identity, IReadOnlyList<IEvent> events)
    {
        var actionType = ActionType.Store;

        if (snapshot == null && events.HasNoEventsOfType<Start>())
        {
            return (snapshot, ActionType.Nothing);
        }

        var eventData = events.ToQueueOfEventData();
        while (eventData.Any())
        {
            var data = eventData.Dequeue();
            switch (data)
            {
                case Start:
                    snapshot = new HardDeletedStartAndStopAggregate2
                    {
                        // Have to assign the identity ourselves
                        Id = identity
                    };
                    break;

                case Increment when snapshot is { }:
                    // Use explicit code to only apply this event
                    // if the snapshot already exists
                    snapshot.Increment();
                    break;

                case End when snapshot is {}:
                    actionType = ActionType.HardDelete;
                    snapshot = null;
                    break;

                case Restart when snapshot == null:
                    // Got to "undo" the soft delete status
                    actionType = ActionType.Store;
                    snapshot = new HardDeletedStartAndStopAggregate2 { Id = identity };
                    break;
            }
        }

        return (snapshot, actionType);
    }


}
