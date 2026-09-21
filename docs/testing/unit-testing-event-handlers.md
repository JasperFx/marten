# Unit testing event-sourced handlers

[Integration testing](/testing/integration) covers the half of your test suite that needs a real
database — persistence, optimistic concurrency, and projection correctness. This page is the other
half: testing the *decision* a command handler makes, with no Postgres involved at all.

A handler that takes an `IEventStream<T>` — through Wolverine's
[aggregate handler workflow](https://wolverinefx.net/guide/durability/marten/event-sourcing.html),
or as the body you hand to `WriteToAggregate()` — is a function from *current aggregate state plus a
command* to *the events that should be appended*. Both halves of that are just objects, so the test
needs nothing more than a stand-in for the stream.

`JasperFx.Events.StubEventStream<T>` is that stand-in. It records appended events in a list instead
of persisting them, and it reads identically across Marten, Polecat and Fisher.

## The handler under test

<!-- snippet: sample_stub_event_stream_subject -->
<a id='snippet-sample_stub_event_stream_subject'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/StubEventStreamTests.cs#L11-L72' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_stub_event_stream_subject' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Asserting on the events a handler appends

Construct the stub with the aggregate state the handler should see, call the handler, and assert on
`EventsAppended`:

<!-- snippet: sample_stub_event_stream_basic_usage -->
<a id='snippet-sample_stub_event_stream_basic_usage'></a>
```cs
// Arrange -- the aggregate state the handler should see. No database, no mocks.
var stream = new StubEventStream<Booking>(new Booking(Guid.NewGuid(), 10, false));

// Act
BookingHandler.Handle(new ReserveSeats(stream.Id, 2), stream);

// Assert on what the handler actually produced
var reserved = stream.EventsAppended.Single().ShouldBeOfType<SeatsReserved>();
reserved.Seats.ShouldBe(2);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/StubEventStreamTests.cs#L79-L91' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_stub_event_stream_basic_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## A stream that does not exist yet

A `null` aggregate is exactly what a handler sees for a stream that has not been started, which is
how it chooses between starting a stream and appending to one:

<!-- snippet: sample_stub_event_stream_missing_stream -->
<a id='snippet-sample_stub_event_stream_missing_stream'></a>
```cs
// A null aggregate is what a handler sees for a stream that has not been started
var stream = new StubEventStream<Booking>(null);

BookingHandler.Handle(new ReserveSeats(Guid.NewGuid(), 4), stream);

stream.EventsAppended.Single().ShouldBeOfType<BookingStarted>()
    .Seats.ShouldBe(4);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/StubEventStreamTests.cs#L97-L107' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_stub_event_stream_missing_stream' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Deciding to do nothing

"This command required no events" is a real outcome and worth its own test:

<!-- snippet: sample_stub_event_stream_nothing_appended -->
<a id='snippet-sample_stub_event_stream_nothing_appended'></a>
```cs
var stream = new StubEventStream<Booking>(new Booking(Guid.NewGuid(), 10, false));

BookingHandler.Handle(new ReserveSeats(stream.Id, 0), stream);

// "The handler decided to do nothing" is a real outcome, and one a mock
// verification of AppendOne() cannot state directly
stream.EventsAppended.ShouldBeEmpty();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/StubEventStreamTests.cs#L113-L123' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_stub_event_stream_nothing_appended' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## A command that spans two streams

A handler that takes two stream handles gets two stubs. Set `Id` — or `Key`, if your streams are
identified by string — so the identities the handler reads line up with the command:

<!-- snippet: sample_stub_event_stream_multiple_streams -->
<a id='snippet-sample_stub_event_stream_multiple_streams'></a>
```cs
// Set Id (or Key, for a string identified stream) so the command's identities
// line up with the streams the handler was handed
var from = new StubEventStream<Booking>(new Booking(Guid.NewGuid(), 10, false)) { Id = Guid.NewGuid() };
var to = new StubEventStream<Booking>(new Booking(Guid.NewGuid(), 0, false)) { Id = Guid.NewGuid() };

BookingHandler.Handle(new TransferSeats(from.Id, to.Id, 3), from, to);

from.EventsAppended.Single().ShouldBeOfType<SeatsReleased>().Seats.ShouldBe(3);
to.EventsAppended.Single().ShouldBeOfType<SeatsReserved>().Seats.ShouldBe(3);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/StubEventStreamTests.cs#L129-L141' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_stub_event_stream_multiple_streams' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## `EventsAppended` vs `Events`

`EventsAppended` holds the raw event bodies the handler emitted, and is what most assertions want.
`Events` wraps those same bodies in `IEvent` envelopes for a handler or assertion that reads the
interface member itself:

<!-- snippet: sample_stub_event_stream_events_vs_events_appended -->
<a id='snippet-sample_stub_event_stream_events_vs_events_appended'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/StubEventStreamTests.cs#L160-L175' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_stub_event_stream_events_vs_events_appended' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The envelopes carry the event type naming and nothing a real store would only know at save time:
no sequence, no stream version, no server timestamp.

## Versions and identity

`Id`, `Key`, `StartingVersion` and `CurrentVersion` are all settable, so a handler whose logic reads
any of them can be exercised:

<!-- snippet: sample_stub_event_stream_versions_and_identity -->
<a id='snippet-sample_stub_event_stream_versions_and_identity'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/StubEventStreamTests.cs#L181-L197' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_stub_event_stream_versions_and_identity' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Both `Id` and `Key` are populated with a new value by default, because the stub cannot know which
identity style the handler under test reads.

## Why not a mocking library?

The obvious alternative is `Substitute.For<IEventStream<Account>>()` followed by
`stream.Received(1).AppendOne(Arg.Any<Withdrawn>())`. That verifies *a method was called*. It says
nothing about **which** event was appended or what it carried — which is the entire content of the
decision the handler makes — and it breaks on a behavior-preserving refactor, such as one
`AppendMany` in place of two `AppendOne` calls.

A recorded list of events is the thing the handler is actually supposed to produce. Asserting on it
survives that refactor and fails when the decision changes, which is the only time you want a unit
test to fail.

## What the stub does not do

::: warning
The stub **records**. It does not persist, project, validate, or apply optimistic concurrency, and
it will never throw `ConcurrencyException`. `TryFastForwardVersion()` is a no-op.
:::

Anything that depends on the database's behavior rather than on your handler's logic belongs in an
[integration test](/testing/integration):

- optimistic concurrency and `ConcurrencyException`
- whether a projection produces the right document from those events
- event type aliases as they are actually persisted and read back
- multi-tenancy, and anything involving the async daemon

## Marten's own `StubEventStream<T>`

`Marten.Events.StubEventStream<T>` has existed since long before the JasperFx one and still works —
it is now a thin subclass that adds a `StoreOptions` overload, for the rare test that asserts on
event type *names* under customized aliases:

```cs
var options = new StoreOptions();
options.Events.MapEventType<SeatsReserved>("seats_taken");

var stream = new Marten.Events.StubEventStream<Booking>(null, options);
stream.AppendOne(new SeatsReserved(2));

stream.Events.Single().EventTypeName.ShouldBe("seats_taken");
```

Reach for `JasperFx.Events.StubEventStream<T>` otherwise. It is the one that reads the same in every
Critter Stack store, and Marten's subclass is expected to be marked obsolete on the next major.

::: tip
The two types have the same name in different namespaces, so a test file with both
`using Marten.Events;` and `using JasperFx.Events;` will not compile until you qualify one of them.
:::
