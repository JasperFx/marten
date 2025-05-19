using System;
using System.Linq;
using JasperFx.Events;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class marking_stream_as_isnew_on_capture: IntegrationContext
{
    [Fact]
    public void isnew_on_start_stream()
    {
        var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        var id = theSession.Events.StartStream<Quest>(joined, departed);

        theSession.PendingChanges.Streams().Single().ActionType.ShouldBe(StreamActionType.Start);
    }

    [Fact]
    public void isnew_on_start_stream_2()
    {
        var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        theSession.Events.StartStream<Quest>(Guid.NewGuid(), joined, departed);

        theSession.PendingChanges.Streams().Single().ActionType.ShouldBe(StreamActionType.Start);
    }

    [Fact]
    public void should_be_existing_stream_on_append_event()
    {
        var joined = new MembersJoined { Members = new string[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };

        theSession.Events.Append(Guid.NewGuid(), joined, departed);

        theSession.PendingChanges.Streams().Single().ActionType
            .ShouldBe(StreamActionType.Append);
    }

    public marking_stream_as_isnew_on_capture(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
