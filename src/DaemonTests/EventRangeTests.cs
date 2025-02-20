using System.Collections.Generic;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Shouldly;
using Xunit;

namespace DaemonTests;

public class EventRangeTests
{
    [Fact]
    public void size_with_no_events()
    {
        var range = new EventRange(new ShardName("name"), 0, 100);
        range.Size.ShouldBe(100);
    }

    [Fact]
    public void size_with_events()
    {
        var range = new EventRange(new ShardName("name"), 0, 100)
        {
            Events = new List<IEvent>
            {
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
            }
        };

        range.Size.ShouldBe(5);
    }

    [Fact]
    public void skip_event_sequence()
    {
        var range = new EventRange(new ShardName("name"), 0, 100)
        {
            Events = new List<IEvent>
            {
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
            }
        };

        var sequence = 111;
        foreach (var @event in range.Events)
        {
            @event.Sequence = sequence++;
        }

        range.SkipEventSequence(114);

        range.Events.Count.ShouldBe(4);
    }

    [Fact]
    public void combine_shallow()
    {
        var range1 = new EventRange(new ShardName("name"), 0, 100)
        {
            Events = new List<IEvent>
            {
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
            }
        };

        var range2 = new EventRange(new ShardName("name"), 100, 200)
        {
            Events = new List<IEvent>
            {
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
            }
        };

        var range3 = new EventRange(new ShardName("name"), 200, 300)
        {
            Events = new List<IEvent>
            {
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
                new Event<AEvent>(new AEvent()),
            }
        };

        var combined = EventRange.CombineShallow(range1, range2, range3);
        combined.SequenceFloor.ShouldBe(0);
        combined.SequenceCeiling.ShouldBe(300);
        combined.ShardName.ShouldBe(range1.ShardName);
    }
}
