# Stream aggregation

In Event Sourcing, the entity state is stored as the series of events that happened for this specific object, e.g. `InvoiceInitiated`, `InvoiceIssued`, `InvoiceSent`.  All of those events shares the stream id, and have incremented stream version. In other words, they're correlated by the stream id ordered by stream position. 

Streams can be thought of as the entities' representation. Traditionally (e.g. in relational or document approach), each entity is stored as a separate record.

To get the current state of entity we need to perform the stream aggregation process (called also _state rehydration_ or _state rebuild_). We're translating the set of events into a single entity. This can be done with the following the steps:
1. Read all events for the specific stream.
2. Order them in ascending order of appearance (by the event's stream position).
3. Construct the empty object of the entity type (e.g. with default constructor).
4. Apply each event on the entity.

 Marten handles this process internally with the `AggregateStreamAsync` method.
 
 The class representing the stream (entity) state has to follow the naming convention. For each event have `Apply` method with:
- single parameter with event object,
- `void` type as the result.

For example, having the Invoice events stream with following events:

<!-- snippet: sample_aggregate-stream-events -->
<a id='snippet-sample_aggregate-stream-events'></a>
```cs
public record InvoiceInitiated(
    Guid InvoiceId,
    double Amount,
    string Number,
    Person IssuedTo,
    DateTime InitiatedAt
);

public record Person(
    string Name,
    string Address
);

public record InvoiceIssued(
    Guid InvoiceId,
    string IssuedBy,
    DateTime IssuedAt
);

public enum InvoiceSendMethod
{
    Email,
    Post
}

public record InvoiceSent(
    Guid InvoiceId,
    InvoiceSendMethod SentVia,
    DateTime SentAt
);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Aggregation/aggregate_stream_samples.cs#L11-L42' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregate-stream-events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

and following entity class definition:

<!-- snippet: sample_aggregate-stream-invoice-entity -->
<a id='snippet-sample_aggregate-stream-invoice-entity'></a>
```cs
public class Invoice
{
    public Guid Id { get; private set; }
    public double Amount { get; private set; }
    public string Number { get; private set; } = default!;

    public InvoiceStatus Status { get; private set; }

    public Person IssuedTo { get; private set; } = default!;
    public DateTime InitiatedAt { get; private set; }

    public string? IssuedBy { get; private set; }
    public DateTime IssuedAt { get; private set; }

    public InvoiceSendMethod SentVia { get; private set; }
    public DateTime SentAt { get; private set; }

    public void Apply(InvoiceInitiated @event)
    {
        Id = @event.InvoiceId;
        Amount = @event.Amount;
        Number = @event.Number;
        IssuedTo = @event.IssuedTo;
        InitiatedAt = @event.InitiatedAt;
        Status = InvoiceStatus.Initiated;
    }

    public void Apply(InvoiceIssued @event)
    {
        IssuedBy = @event.IssuedBy;
        IssuedAt = @event.IssuedAt;
        Status = InvoiceStatus.Issued;
    }

    public void Apply(InvoiceSent @event)
    {
        SentVia = @event.SentVia;
        SentAt = @event.SentAt;
        Status = InvoiceStatus.Sent;
    }
}

public enum InvoiceStatus
{
    Initiated = 1,
    Issued = 2,
    Sent = 3
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Aggregation/aggregate_stream_samples.cs#L44-L94' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregate-stream-invoice-entity' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To retrieve the state it's enough to call:

<!-- snippet: sample_aggregate-stream-usage -->
<a id='snippet-sample_aggregate-stream-usage'></a>
```cs
var invoice = await theSession.Events.AggregateStreamAsync<Invoice>(invoiceId);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Aggregation/aggregate_stream_samples.cs#L124-L126' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregate-stream-usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Time Travelling

One of the most significant advantages of Event Sourcing is that you're not losing any data. Each event represents the change made at a certain point in time. Thank that you can do time travelling to get the state at a specific date or stream version. 

That capability enables rich diagnostics business and technical wise. You can precisely verify what has happened in your system and troubleshoot the failing scenario.

You can also do business reports analysing the state at a particular time and make predictions based on that.

For example, having a stream representing the rooms' availability in hotel defined as:

<!-- snippet: sample_aggregate-stream-time-travelling-definition -->
<a id='snippet-sample_aggregate-stream-time-travelling-definition'></a>
```cs
public enum RoomType
{
    Single,
    Double,
    King
}

public record HotelRoomsDefined(
    Guid HotelId,
    Dictionary<RoomType, int> RoomTypeCounts
);

public record RoomBooked(
    Guid HotelId,
    RoomType RoomType
);

public record GuestCheckedOut(
    Guid HotelId,
    Guid GuestId,
    RoomType RoomType
);

public class RoomsAvailability
{
    public Guid Id { get; private set; }

    public int AvailableSingleRooms => roomTypeCounts[RoomType.Single];
    public int AvailableDoubleRooms => roomTypeCounts[RoomType.Double];
    public int AvailableKingRooms => roomTypeCounts[RoomType.King];

    private Dictionary<RoomType, int> roomTypeCounts { get; set; }

    public void Apply(HotelRoomsDefined @event)
    {
        Id = @event.HotelId;
        roomTypeCounts = @event.RoomTypeCounts;
    }

    public void Apply(RoomBooked @event)
    {
        roomTypeCounts[@event.RoomType] -= 1;
    }

    public void Apply(GuestCheckedOut @event)
    {
        roomTypeCounts[@event.RoomType] += 1;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Aggregation/time_travelling_samples.cs#L11-L63' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregate-stream-time-travelling-definition' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

**You can get the stream state at the point of time, providing a timestamp:**

<!-- snippet: sample_aggregate-stream-time-travelling-by-point-of-time -->
<a id='snippet-sample_aggregate-stream-time-travelling-by-point-of-time'></a>
```cs
var roomsAvailabilityAtPointOfTime =
    await theSession.Events
        .AggregateStreamAsync<RoomsAvailability>(hotelId, timestamp: pointOfTime);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Aggregation/time_travelling_samples.cs#L122-L128' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregate-stream-time-travelling-by-point-of-time' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


**Or specific version:**

<!-- snippet: sample_aggregate-stream-time-travelling-by-specific-version -->
<a id='snippet-sample_aggregate-stream-time-travelling-by-specific-version'></a>
```cs
var roomsAvailabilityAtVersion =
    await theSession.Events
        .AggregateStreamAsync<RoomsAvailability>(hotelId, version: specificVersion);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Aggregation/time_travelling_samples.cs#L136-L142' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregate-stream-time-travelling-by-specific-version' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Aggregating state into

TO DO
