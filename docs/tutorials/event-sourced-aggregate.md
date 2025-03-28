# Part 3: Building an Event-Sourced Aggregate

Now that we are recording events for each shipment, we need a way to derive the latest state of the shipment from those events. In event sourcing, an **aggregate** is an entity that can **replay or apply events** to build up its current state. In our case, the `FreightShipment` is the aggregate, and we want to be able to construct a `FreightShipment` object by applying the `ShipmentScheduled`, `ShipmentPickedUp`, etc. events in sequence.

Fortunately, Marten can do a lot of this heavy lifting for us if we define how our `FreightShipment` applies events. Marten follows a convention of using static factory and static apply methods to define aggregates. This approach provides explicit control and is the most idiomatic way of building aggregates with Marten.

### Defining the Aggregate with Static Apply Methods

```csharp
public class FreightShipment
{
    public Guid Id { get; private set; }
    public string Origin { get; private set; }
    public string Destination { get; private set; }
    public ShipmentStatus Status { get; private set; }
    public DateTime ScheduledAt { get; private set; }
    public DateTime? PickedUpAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string CancellationReason { get; private set; }

    public static FreightShipment Create(ShipmentScheduled @event)
    {
        return new FreightShipment
        {
            Id = @event.ShipmentId,
            Origin = @event.Origin,
            Destination = @event.Destination,
            Status = ShipmentStatus.Scheduled,
            ScheduledAt = @event.ScheduledAt
        };
    }

    public static FreightShipment Apply(FreightShipment current, ShipmentPickedUp @event)
    {
        current.Status = ShipmentStatus.InTransit;
        current.PickedUpAt = @event.PickedUpAt;
        return current;
    }

    public static FreightShipment Apply(FreightShipment current, ShipmentDelivered @event)
    {
        current.Status = ShipmentStatus.Delivered;
        current.DeliveredAt = @event.DeliveredAt;
        return current;
    }

    public static FreightShipment Apply(FreightShipment current, ShipmentCancelled @event)
    {
        current.Status = ShipmentStatus.Cancelled;
        current.CancelledAt = @event.CancelledAt;
        current.CancellationReason = @event.Reason;
        return current;
    }
}
```

### Comparison to Document Modeling

Compared to our earlier document models in `Shipment` and `Driver`, this aggregate model differs in a few key ways:

- The aggregate is built from **events**, not manually instantiated.
- Instead of using `set` properties and constructors to initialize the object directly, we use **event-driven construction** via `Create(...)`.
- State transitions are handled using **explicit `Apply(...)` methods**, each corresponding to a domain event.

This approach enforces a clear, consistent flow of state changes and ensures that aggregates are always built in a valid state based on domain history. It also enables Marten to recognize and apply the projection automatically during event replay or live aggregation.

### Why Static Apply and Create?

- **Static `Create`**: Marten uses this method to initialize the aggregate from the first event in the stream. It must return a fully constructed aggregate.
- **Static `Apply`**: Each subsequent event in the stream is passed to the relevant `Apply` method to update the current instance. These methods must return the updated aggregate.
- This design avoids mutation during construction and encourages an immutable mindset. It also aligns with Marten's default code generation and improves transparency when reading aggregate logic.

By adopting this convention, your aggregate classes are fully compatible with Marten’s projection engine and behave consistently across live and inline projections.

Now that our `FreightShipment` can be built from events, let’s use Marten to do exactly that. We have two main ways to get the current state from events:
1. **On-demand aggregation** – load events and aggregate them in memory when needed.
2. **Projections (stored aggregation)** – have Marten automatically update a stored `FreightShipment` document as events come in (so we can load it directly like a regular document).

We’ll explore both, starting with on-demand aggregation.

## Aggregating a Stream on Demand (Live Projection)

Marten provides an easy way to aggregate a stream of events into an object: `AggregateStreamAsync<T>()`. We can use this to fetch the latest state without having stored a document. For example:

```csharp
// Assuming we have a stream of events for shipmentId (from earlier Part)
var currentState = await session.Events.AggregateStreamAsync<FreightShipment>(shipmentId);
Console.WriteLine($"State: {currentState.Status}, PickedUpAt: {currentState.PickedUpAt}");
```

When this code runs, Marten will fetch all events for the given stream ID from the database, then create a `FreightShipment` and apply each event (using the `Apply` methods we defined) to produce `currentState`. If our earlier events were Scheduled, PickedUp, Delivered, the resulting `currentState` should have `Status = Delivered` and the `PickedUpAt`/`DeliveredAt` times set appropriately.

This on-demand approach is an example of a **live projection** – we compute the projection (the aggregate) from raw events in real time, without storing the result. Marten even lets you run live aggregations over a selection of events via LINQ queries (using the `AggregateToAsync<T>` operator) ([Live Aggregation | Marten](https://martendb.io/events/projections/live-aggregates#:~:text=Marten%20V4%20introduces%20a%20mechanism,as%20shown%20below)). For instance, you could query a subset of events (perhaps filtering by date or type) and aggregate those on the fly. This is powerful for ad-hoc queries or scenarios where you don’t need to frequently read the same aggregate.

However, recalculating an aggregate from scratch each time can be inefficient if the event stream is long or if you need to read the state often. In our shipping example, if we have to show the current status of shipments frequently (e.g., in a UI dashboard), constantly replaying events might be overkill, especially as the number of events grows. This is where Marten’s **projection** support shines, allowing us to maintain a persistent up-to-date view of the aggregate.

## Inline Projections: Mixing Events with Documents

In some scenarios, we want Marten to automatically maintain a document that reflects the latest state of a single stream of events. This can be useful for dashboards or APIs where fast reads are important and you don’t want to replay the entire stream on every request.

To do this, we can register a `SingleStreamProjection<T>` and configure it to run inline. This projection class applies events from one stream and writes the resulting document to the database as part of the same transaction.

```csharp
public class ShipmentView
{
    public Guid Id { get; set; }
    public string Origin { get; set; }
    public string Destination { get; set; }
    public string Status { get; set; }
    public DateTime? PickedUpAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}

public class ShipmentViewProjection : SingleStreamProjection<ShipmentView>
{
    public ShipmentView Create(ShipmentScheduled @event) => new ShipmentView
    {
        Id = @event.ShipmentId,
        Origin = @event.Origin,
        Destination = @event.Destination,
        Status = "Scheduled"
    };

    public void Apply(ShipmentView view, ShipmentPickedUp @event)
    {
        view.Status = "InTransit";
        view.PickedUpAt = @event.PickedUpAt;
    }

    public void Apply(ShipmentView view, ShipmentDelivered @event)
    {
        view.Status = "Delivered";
        view.DeliveredAt = @event.DeliveredAt;
    }
}
```

Register the projection during configuration:

```csharp
var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);
    opts.Projections.Add<ShipmentViewProjection>(ProjectionLifecycle.Inline);
});
```

Let’s illustrate how this works with our shipment example:


```csharp
using var session = store.LightweightSession();

var sid = Guid.NewGuid();
var evt1 = new ShipmentScheduled(sid, "Los Angeles", "Tokyo", DateTime.UtcNow);
session.Events.StartStream<ShipmentView>(sid, evt1);
await session.SaveChangesAsync();  // Inserts initial ShipmentView

var evt2 = new ShipmentPickedUp(DateTime.UtcNow.AddHours(2));
session.Events.Append(sid, evt2);
await session.SaveChangesAsync();  // Updates ShipmentView.Status and PickedUpAt

var doc = await session.LoadAsync<ShipmentView>(sid);
Console.WriteLine(doc.Status);         // InTransit
Console.WriteLine(doc.PickedUpAt);    // Set to pickup time
```

This flow shows that each time we append an event, Marten applies the changes immediately and updates the document inside the same transaction. This ensures strong consistency between the event stream and the projected view.

Note that this is not the same as aggregating a domain model like `FreightShipment` using `AggregateStreamAsync<T>`. Instead, we're producing a derived view (or read model) designed for fast queries, based on a subset of event data.