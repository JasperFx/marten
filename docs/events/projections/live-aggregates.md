# Live Aggregation

::: tip
For information on how to create aggregated projection or "self-aggregates," see [Aggregate Projections](/events/projections/aggregate-projections).
:::


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

One of the most significant advantages of Event Sourcing is that you're not losing any data. Each event represents the change made at a certain point in time. This allows you to do time travelling to get the state at a specific date or stream version. 

This capability enables rich diagnostics business and technical wise. You can precisely verify what has happened in your system and troubleshoot the failing scenario.

You can also do business reports analyzing the state at a particular time and make predictions based on that.

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

    private Dictionary<RoomType, int> roomTypeCounts = new ();

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Aggregation/time_travelling_samples.cs#L12-L64' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregate-stream-time-travelling-definition' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

**You can get the stream state at the point of time, providing a timestamp:**

<!-- snippet: sample_aggregate-stream-time-travelling-by-point-of-time -->
<a id='snippet-sample_aggregate-stream-time-travelling-by-point-of-time'></a>
```cs
var roomsAvailabilityAtPointOfTime =
    await theSession.Events
        .AggregateStreamAsync<RoomsAvailability>(hotelId, timestamp: pointOfTime);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Aggregation/time_travelling_samples.cs#L123-L129' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregate-stream-time-travelling-by-point-of-time' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


**Or specific version:**

<!-- snippet: sample_aggregate-stream-time-travelling-by-specific-version -->
<a id='snippet-sample_aggregate-stream-time-travelling-by-specific-version'></a>
```cs
var roomsAvailabilityAtVersion =
    await theSession.Events
        .AggregateStreamAsync<RoomsAvailability>(hotelId, version: specificVersion);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Aggregation/time_travelling_samples.cs#L137-L143' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregate-stream-time-travelling-by-specific-version' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Aggregating Events into Existing State

Marten also allows aggregating the stream into a specific entity instance. This means that a particular set of events are taken and applied to an object one by one in the same order of occurrence. To achieve it, you should pass the base entity state as a `state` parameter into the `AggregateStream` method.

<!-- snippet: sample_aggregate-stream-into-state-default -->
<a id='snippet-sample_aggregate-stream-into-state-default'></a>
```cs
await theSession.Events.AggregateStreamAsync(
    streamId,
    state: baseState,
    fromVersion: baseStateVersion
);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Aggregation/aggregate_stream_into_samples.cs#L141-L147' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregate-stream-into-state-default' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

It can be helpful, for instance, in snapshotting. Snapshot is a state of the stream at a specific point of time (version). It is a performance optimization that shouldn't be your first choice, but it's an option to consider for performance-critical computations. As you're optimizing your processing, you usually don't want to store a snapshot after each event not to increase the number of writes. Usually, you'd like to do a snapshot on the specific interval or specific event type.

Let's take the financial account as an example.

<!-- snippet: sample_aggregate-stream-into-state-definition -->
<a id='snippet-sample_aggregate-stream-into-state-definition'></a>
```cs
public record AccountingMonthOpened(
    Guid FinancialAccountId,
    int Month,
    int Year,
    decimal StartingBalance
);

public record InflowRecorded(
    Guid FinancialAccountId,
    decimal TransactionAmount
);

public record CashWithdrawnFromATM(
    Guid FinancialAccountId,
    decimal CashAmount
);

public record AccountingMonthClosed(
    Guid FinancialAccountId,
    int Month,
    int Year,
    decimal FinalBalance
);

public class FinancialAccount
{
    public Guid Id { get; private set; }
    public int CurrentMonth { get; private set; }
    public int CurrentYear { get; private set; }
    public bool IsOpened { get; private set; }
    public decimal Balance { get; private set; }
    public int Version { get; private set; }

    public void Apply(AccountingMonthOpened @event)
    {
        Id = @event.FinancialAccountId;
        CurrentMonth = @event.Month;
        CurrentYear = @event.Year;
        Balance = @event.StartingBalance;
        IsOpened = true;
        Version++;
    }

    public void Apply(InflowRecorded @event)
    {
        Balance += @event.TransactionAmount;

        Version++;
    }

    public void Apply(CashWithdrawnFromATM @event)
    {
        Balance -= @event.CashAmount;
        Version++;
    }

    public void Apply(AccountingMonthClosed @event)
    {
        IsOpened = false;
        Version++;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Aggregation/aggregate_stream_into_samples.cs#L13-L78' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregate-stream-into-state-definition' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For the daily operations, you don't need to know its whole history. It's enough to have information about the current accounting period, e.g. month. It might be worth doing a snapshot of the current state at opening accounting and then loading the following events with the transactions. We could do it by defining such a wrapper class:


<!-- snippet: sample_aggregate-stream-into-state-wrapper -->
<a id='snippet-sample_aggregate-stream-into-state-wrapper'></a>
```cs
public class CashRegisterRepository
{
    private IDocumentSession session;

    public CashRegisterRepository(IDocumentSession session)
    {
        this.session = session;
    }

    public Task Store(
        FinancialAccount financialAccount,
        object @event,
        CancellationToken ct = default
    )
    {
        if (@event is AccountingMonthOpened)
        {
            session.Store(financialAccount);
        }

        session.Events.Append(financialAccount.Id, @event);

        return session.SaveChangesAsync(ct);
    }

    public async Task<FinancialAccount?> Get(
        Guid cashRegisterId,
        CancellationToken ct = default
    )
    {
        var cashRegister =
            await session.LoadAsync<FinancialAccount>(cashRegisterId, ct);

        var fromVersion = cashRegister != null
            ?
            // incrementing version to not apply the same event twice
            cashRegister.Version + 1
            : 0;

        return await session.Events.AggregateStreamAsync(
            cashRegisterId,
            state: cashRegister,
            fromVersion: fromVersion,
            token: ct
        );
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Aggregation/aggregate_stream_into_samples.cs#L81-L131' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregate-stream-into-state-wrapper' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Then append event and store snapshot on opening accounting month:

<!-- snippet: sample_aggregate-stream-into-state-store -->
<a id='snippet-sample_aggregate-stream-into-state-store'></a>
```cs
(FinancialAccount, AccountingMonthOpened) OpenAccountingMonth(
    FinancialAccount cashRegister)
{
    var @event = new AccountingMonthOpened(
        cashRegister.Id, 11, 2021, 300);

    cashRegister.Apply(@event);
    return (cashRegister, @event);
}

var closedCashierShift =
    await theSession.Events.AggregateStreamAsync<FinancialAccount>(
        financialAccountId
    );

var (openedCashierShift, cashierShiftOpened) =
    OpenAccountingMonth(closedCashierShift!);

var repository = new CashRegisterRepository(_session);

await repository.Store(openedCashierShift, cashierShiftOpened);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Aggregation/aggregate_stream_into_samples.cs#L164-L188' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregate-stream-into-state-store' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

and read snapshot and following event with:

<!-- snippet: sample_aggregate-stream-into-state-get -->
<a id='snippet-sample_aggregate-stream-into-state-get'></a>
```cs
var currentState = await repository.Get(financialAccountId);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Aggregation/aggregate_stream_into_samples.cs#L207-L211' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregate-stream-into-state-get' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Live Aggregation from Linq Queries

Marten V4 introduces a mechanism to run a live aggregation to any arbitrary segment of events through
a Linq operator in Marten called `AggregateTo()` or `AggregateToAsync()` as shown below:

<!-- snippet: sample_aggregateto_async_usage_with_linq -->
<a id='snippet-sample_aggregateto_async_usage_with_linq'></a>
```cs
var questParty = await theSession.Events
    .QueryAllRawEvents()

    // You could of course chain all the Linq
    // Where()/OrderBy()/Take()/Skip() operators
    // you need here

    .AggregateToAsync<QuestParty>();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/aggregateto_linq_operator_tests.cs#L38-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_aggregateto_async_usage_with_linq' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

These methods are extension methods in the `Marten.Events` namespace.
