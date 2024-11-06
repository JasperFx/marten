using System;
using System.Collections.Generic;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Internal;
using Marten.Storage;
using NSubstitute;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class StreamActionTester
{
    private readonly IMartenSession theSession;
    private readonly Tenant theTenant;
    private readonly EventGraph theEvents;

    public StreamActionTester()
    {
        theSession = Substitute.For<IMartenSession>();
        theSession.TenantId.Returns("TX");
        theSession.Database.Returns(Substitute.For<IMartenDatabase>());

        theEvents = new EventGraph(new StoreOptions());
    }

    [Fact]
    public void for_determines_action_type_guid()
    {
        var events = new List<IEvent>
        {
            new Event<AEvent>(new AEvent()),
            new Event<AEvent>(new AEvent()),
            new Event<AEvent>(new AEvent()),
            new Event<AEvent>(new AEvent()),
            new Event<AEvent>(new AEvent()),
        };

        events[0].Version = 5;

        StreamAction.For(Guid.NewGuid(), events)
            .ActionType.ShouldBe(StreamActionType.Append);

        events[0].Version = 1;

        StreamAction.For(Guid.NewGuid(), events)
            .ActionType.ShouldBe(StreamActionType.Start);
    }

    [Fact]
    public void for_determines_action_type_string()
    {
        var events = new List<IEvent>
        {
            new Event<AEvent>(new AEvent()),
            new Event<AEvent>(new AEvent()),
            new Event<AEvent>(new AEvent()),
            new Event<AEvent>(new AEvent()),
            new Event<AEvent>(new AEvent()),
        };

        events[0].Version = 5;

        StreamAction.For(Guid.NewGuid().ToString(), events)
            .ActionType.ShouldBe(StreamActionType.Append);

        events[0].Version = 1;

        StreamAction.For(Guid.NewGuid().ToString(), events)
            .ActionType.ShouldBe(StreamActionType.Start);
    }

    [Fact]
    public void ApplyServerVersion_for_new_streams()
    {
        var action = StreamAction.Start(theEvents, Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());

        var queue = new Queue<long>();
        queue.Enqueue(11);
        queue.Enqueue(12);
        queue.Enqueue(13);
        queue.Enqueue(14);
        action.PrepareEvents(0, theEvents, queue, theSession);


        action.Events[0].Version.ShouldBe(1);
        action.Events[1].Version.ShouldBe(2);
        action.Events[2].Version.ShouldBe(3);
        action.Events[3].Version.ShouldBe(4);

        action.Events[0].Sequence.ShouldBe(11);
        action.Events[1].Sequence.ShouldBe(12);
        action.Events[2].Sequence.ShouldBe(13);
        action.Events[3].Sequence.ShouldBe(14);
    }



    [Fact]
    public void ApplyServerVersion_for_existing_streams()
    {
        var action = StreamAction.Append(theEvents, Guid.NewGuid(), new AEvent(), new BEvent(), new CEvent(), new DEvent());

        var queue = new Queue<long>();
        queue.Enqueue(11);
        queue.Enqueue(12);
        queue.Enqueue(13);
        queue.Enqueue(14);


        action.PrepareEvents(5, theEvents, queue, theSession);

        action.ExpectedVersionOnServer.ShouldBe(5);


        action.Events[0].Version.ShouldBe(6);
        action.Events[1].Version.ShouldBe(7);
        action.Events[2].Version.ShouldBe(8);
        action.Events[3].Version.ShouldBe(9);
    }
}
