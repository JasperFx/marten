using System;
using System.Linq;
using EventSourcingTests.Projections;
using JasperFx.Events;
using Marten;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

#region sample_stub_event_stream_subject

public record Booking(Guid Id, int Seats, bool IsCancelled);

public record ReserveSeats(Guid BookingId, int Seats);

public record TransferSeats(Guid FromBookingId, Guid ToBookingId, int Seats);

public record BookingStarted(int Seats);

public record SeatsReserved(int Seats);

public record SeatsReleased(int Seats);

public record BookingRejected(string Reason);

public static class BookingHandler
{
    // Wolverine's aggregate handler workflow hands the handler a live stream handle:
    //     public static void Handle(ReserveSeats command, [WriteAggregate] IEventStream<Booking> stream)
    // and inside Marten itself this is the body you would pass to WriteToAggregate().
    public static void Handle(ReserveSeats command, IEventStream<Booking> stream)
    {
        var booking = stream.Aggregate;

        // A null aggregate is a stream that does not exist yet
        if (booking is null)
        {
            stream.AppendOne(new BookingStarted(command.Seats));
            return;
        }

        if (booking.IsCancelled)
        {
            stream.AppendOne(new BookingRejected("Booking has been cancelled"));
            return;
        }

        // Asking for nothing is not an error, it is just nothing to record
        if (command.Seats == 0)
        {
            return;
        }

        stream.AppendOne(new SeatsReserved(command.Seats));
    }

    // A command that spans two streams takes two stream handles
    public static void Handle(TransferSeats command, IEventStream<Booking> from, IEventStream<Booking> to)
    {
        if (from.Id == to.Id)
        {
            from.AppendOne(new BookingRejected("Cannot transfer seats to the same booking"));
            return;
        }

        from.AppendOne(new SeatsReleased(command.Seats));
        to.AppendOne(new SeatsReserved(command.Seats));
    }
}

#endregion

public class StubEventStreamTests
{
    [Fact]
    public void the_happy_path()
    {
        #region sample_stub_event_stream_basic_usage

        // Arrange -- the aggregate state the handler should see. No database, no mocks.
        var stream = new StubEventStream<Booking>(new Booking(Guid.NewGuid(), 10, false));

        // Act
        BookingHandler.Handle(new ReserveSeats(stream.Id, 2), stream);

        // Assert on what the handler actually produced
        var reserved = stream.EventsAppended.Single().ShouldBeOfType<SeatsReserved>();
        reserved.Seats.ShouldBe(2);

        #endregion
    }

    [Fact]
    public void the_stream_does_not_exist_yet()
    {
        #region sample_stub_event_stream_missing_stream

        // A null aggregate is what a handler sees for a stream that has not been started
        var stream = new StubEventStream<Booking>(null);

        BookingHandler.Handle(new ReserveSeats(Guid.NewGuid(), 4), stream);

        stream.EventsAppended.Single().ShouldBeOfType<BookingStarted>()
            .Seats.ShouldBe(4);

        #endregion
    }

    [Fact]
    public void nothing_to_append()
    {
        #region sample_stub_event_stream_nothing_appended

        var stream = new StubEventStream<Booking>(new Booking(Guid.NewGuid(), 10, false));

        BookingHandler.Handle(new ReserveSeats(stream.Id, 0), stream);

        // "The handler decided to do nothing" is a real outcome, and one a mock
        // verification of AppendOne() cannot state directly
        stream.EventsAppended.ShouldBeEmpty();

        #endregion
    }

    [Fact]
    public void a_command_that_spans_two_streams()
    {
        #region sample_stub_event_stream_multiple_streams

        // Set Id (or Key, for a string identified stream) so the command's identities
        // line up with the streams the handler was handed
        var from = new StubEventStream<Booking>(new Booking(Guid.NewGuid(), 10, false)) { Id = Guid.NewGuid() };
        var to = new StubEventStream<Booking>(new Booking(Guid.NewGuid(), 0, false)) { Id = Guid.NewGuid() };

        BookingHandler.Handle(new TransferSeats(from.Id, to.Id, 3), from, to);

        from.EventsAppended.Single().ShouldBeOfType<SeatsReleased>().Seats.ShouldBe(3);
        to.EventsAppended.Single().ShouldBeOfType<SeatsReserved>().Seats.ShouldBe(3);

        #endregion
    }

    [Fact]
    public void the_two_streams_are_the_same_stream()
    {
        var id = Guid.NewGuid();
        var from = new StubEventStream<Booking>(new Booking(id, 10, false)) { Id = id };
        var to = new StubEventStream<Booking>(new Booking(id, 10, false)) { Id = id };

        BookingHandler.Handle(new TransferSeats(id, id, 3), from, to);

        from.EventsAppended.Single().ShouldBeOfType<BookingRejected>();
        to.EventsAppended.ShouldBeEmpty();
    }

    [Fact]
    public void events_appended_versus_event_envelopes()
    {
        #region sample_stub_event_stream_events_vs_events_appended

        var stream = new StubEventStream<Booking>(new Booking(Guid.NewGuid(), 10, false));
        BookingHandler.Handle(new ReserveSeats(stream.Id, 2), stream);

        // EventsAppended holds the raw event bodies the handler emitted -- what most
        // assertions want
        stream.EventsAppended.Single().ShouldBeOfType<SeatsReserved>();

        // Events wraps those same bodies in IEvent envelopes, for a handler or an
        // assertion that reads the interface member itself
        var envelope = stream.Events.Single();
        envelope.Data.ShouldBeOfType<SeatsReserved>();
        envelope.EventType.ShouldBe(typeof(SeatsReserved));

        #endregion
    }

    [Fact]
    public void versions_are_settable_for_a_version_sensitive_handler()
    {
        #region sample_stub_event_stream_versions_and_identity

        var stream = new StubEventStream<Booking>(new Booking(Guid.NewGuid(), 10, false))
        {
            // The identity the handler reads off the stream
            Id = Guid.NewGuid(),
            Key = "booking-1",

            // The stream version the handler sees, for logic that cares about it
            StartingVersion = 4,
            CurrentVersion = 4
        };

        stream.StartingVersion.ShouldBe(4);
        stream.CurrentVersion.ShouldBe(4);

        #endregion
    }

    [Fact]
    public void append_one()
    {
        // Marten's own subclass of the JasperFx stub, kept for the StoreOptions overload
        var stream = new Marten.Events.StubEventStream<QuestParty>(new QuestParty());
        stream.AppendOne(new QuestStarted());

        stream.EventsAppended.Count.ShouldBe(1);
    }

    [Fact]
    public void append_many()
    {
        var stream = new Marten.Events.StubEventStream<QuestParty>(new QuestParty());
        stream.AppendMany(new QuestStarted(), new MonsterDefeated());
        stream.EventsAppended.Count.ShouldBe(2);
    }

    [Fact]
    public void marten_stub_honors_configured_event_aliases()
    {
        var options = new StoreOptions();
        options.Events.MapEventType<SeatsReserved>("seats_taken");

        var stream = new Marten.Events.StubEventStream<Booking>(null, options);
        stream.AppendOne(new SeatsReserved(2));

        stream.Events.Single().EventTypeName.ShouldBe("seats_taken");
    }
}
