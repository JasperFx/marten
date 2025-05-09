# Part 5: Multi-Stream Projections – Cross-Aggregate Views

So far, each projection we considered was confined to a single stream (one shipment’s events). What if we want to derive insights that span across many shipments? For example, imagine we want a daily report of how many shipments were delivered on each day. This requires looking at `ShipmentDelivered` events from all shipment streams and grouping them by date.

Marten supports this with [Multi Stream Projections](/events/projections/multi-stream-projections). A multi-stream projection processes events from *multiple* streams and aggregates them into one or more view documents. Essentially, you define how to group events (by some key) and how to apply events to a collective view.

## Example: Daily Deliveries Count

Let’s create a projection to count deliveries per day. We’ll make a view document called `DailyShipmentsDelivered` that has a date and a count of delivered shipments for that date.

```csharp
public class DailyShipmentsDelivered
{
    public DateOnly Id { get; set; }        // using DateOnly as the document Id (the day)
    public int DeliveredCount { get; set; }
}
```

We will use the date (year-month-day) as the identity for this document. Each `DailyShipmentsDelivered` document will represent one calendar day. Now we need a projection that listens to `ShipmentDelivered` events from any shipment stream and updates the count for the corresponding day.

Marten’s `MultiStreamProjection<TDoc, TId>` base class makes this easier. We can subclass it:

```csharp
using Marten.Events.Projections;

public class DailyShipmentsProjection : MultiStreamProjection<DailyShipmentsDelivered, DateOnly>
{
    public DailyShipmentsProjection()
    {
        // Group events by the DateOnly key (extracted from DeliveredAt)
        Identity<ShipmentDelivered>(e => DateOnly.FromDateTime(e.DeliveredAt));
    }

    public DailyShipmentsDelivered Create(ShipmentDelivered @event)
    {
        // Create a new view for the date if none exists
        return new DailyShipmentsDelivered 
        {
            Id = DateOnly.FromDateTime(@event.DeliveredAt),
            DeliveredCount = 1
        };
    }

    public void Apply(ShipmentDelivered @event, DailyShipmentsDelivered view)
    {
        // Increment the count for this date
        view.DeliveredCount += 1;
    }
}
```

In this `DailyShipmentsProjection`:

- We use `Identity<ShipmentDelivered>(Func<ShipmentDelivered, DateOnly>)` to tell Marten how to determine the grouping key (the `Id` of our view document) for each `ShipmentDelivered` event. Here we take the event’s timestamp and convert it to a DateOnly (year-month-day) – that’s our grouping key.
- The `Create` method specifies what to do if an event arrives for a date that doesn’t yet have a document. We create a new `DailyShipmentsDelivered` with count 1.
- The `Apply` method defines how to update an existing document when another event for that same date arrives – we just increment the counter.

We would register this projection as typically **async** (since multi-stream projections are by default registered async for safety):

```csharp
opts.Projections.Add<DailyShipmentsProjection>(ProjectionLifecycle.Async);
```

With this in place, whenever a `ShipmentDelivered` event is stored, the async projection daemon will eventually invoke our projection. All delivered events on the same day will funnel into the same `DailyShipmentsDelivered` document (with Id = that date). Marten ensures that events are processed in order and handles concurrency so that our counts don’t collide (under high load, async projection uses locking to avoid race conditions, which is one reason multi-stream is best as async).

After running the system for a while, we could query the daily deliveries:

```csharp
// Example query: get the last 7 days of delivery counts
var lastWeek = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
var stats = await session.Query<DailyShipmentsDelivered>()
                         .Where(x => x.Id >= lastWeek)
                         .OrderBy(x => x.Id)
                         .ToListAsync();

foreach(var dayStat in stats)
{
    Console.WriteLine($"{dayStat.Id}: {dayStat.DeliveredCount} deliveries");
}
```

This query is hitting a regular document table (`DailyShipmentsDelivered` documents), which Marten has been keeping up-to-date from the events. Under the covers, Marten’s projection daemon fetched new `ShipmentDelivered` events, grouped them by date key, and stored/updated the documents.

This example shows the power of combining events from many streams. We could similarly create projections for other cross-cutting concerns, such as:

- Total live shipments in transit per route or per region.
- A table of all cancellations with reasons, to analyze why shipments get cancelled.
- Anything that involves correlating multiple aggregates’ events.

All of it can be done with Marten using the event data we already collect, without additional external ETL jobs. And because it’s within the Marten/PostgreSQL environment, it benefits from transactional safety (the projection daemon will not lose events; it will resume from where it left off if the app restarts, etc.).

**Tip:** For multi-stream projections, consider the volume of data for each grouping key. Our daily summary is a natural grouping (there’s a finite number of days, and each day gets a cumulative count). If you tried to use a highly unique key (like each event creating its own group), that might just be a degenerate case of one event per group – which could have been done as individual documents anyway. Use multi-stream grouping when events truly need to be summarized or combined.

Now that we’ve seen how Marten handles documents, single-stream aggregates, and multi-stream projections, let’s discuss how Marten integrates with an external library called **Wolverine** to scale out and manage these projections in a robust way.
