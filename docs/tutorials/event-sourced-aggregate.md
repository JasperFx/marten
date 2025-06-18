# Part 3: Building an Event-Sourced Aggregate

Now that we are recording events for each shipment, we need a way to derive the latest state of the shipment from those events. In event sourcing, an **aggregate** is an entity that can **replay or apply events** to build up its current state. In our case, the `FreightShipment` is the aggregate, and we want to be able to construct a `FreightShipment` object by applying the `ShipmentScheduled`, `ShipmentPickedUp`, etc. events in sequence.

Fortunately, Marten can do a lot of this heavy lifting for us if we define how our `FreightShipment` applies events. Marten follows a convention of using static factory and static apply methods to define aggregates. This approach provides explicit control and is the most idiomatic way of building aggregates with Marten.

## Defining the Aggregate with Static Apply Methods

<<< @/src/samples/FreightShipping/EventSourcedAggregate.cs#define-aggregate

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

<<< @/src/samples/FreightShipping/EventSourcedAggregate.cs#live-aggregate

When this code runs, Marten will fetch all events for the given stream ID from the database, then create a `FreightShipment` and apply each event (using the `Apply` methods we defined) to produce `currentState`. If our earlier events were Scheduled, PickedUp, Delivered, the resulting `currentState` should have `Status = Delivered` and the `PickedUpAt`/`DeliveredAt` times set appropriately.

This on-demand approach is an example of a **live projection** – we compute the projection (the aggregate) from raw events in real time, without storing the result. Marten even lets you run live aggregations over a selection of events via LINQ queries (using the `AggregateToAsync<T>` operator) ([Live Aggregation](/events/projections/live-aggregates)). For instance, you could query a subset of events (perhaps filtering by date or type) and aggregate those on the fly. This is powerful for ad-hoc queries or scenarios where you don’t need to frequently read the same aggregate.

However, recalculating an aggregate from scratch each time can be inefficient if the event stream is long or if you need to read the state often. In our shipping example, if we have to show the current status of shipments frequently (e.g., in a UI dashboard), constantly replaying events might be overkill, especially as the number of events grows. This is where Marten’s **projection** support shines, allowing us to maintain a persistent up-to-date view of the aggregate.

## Inline Projections: Mixing Events with Documents

In some scenarios, we want Marten to automatically maintain a document that reflects the latest state of a single stream of events. This can be useful for dashboards or APIs where fast reads are important and you don’t want to replay the entire stream on every request.

To do this, we can register a `SingleStreamProjection<T>` and configure it to run inline. This projection class applies events from one stream and writes the resulting document to the database as part of the same transaction.

<<< @/src/samples/FreightShipping/EventSourcedAggregate.cs#single-stream-projection

Register the projection during configuration:

<<< @/src/samples/FreightShipping/EventSourcedAggregate.cs#store-setup

Let’s illustrate how this works with our shipment example:

<<< @/src/samples/FreightShipping/EventSourcedAggregate.cs#shipment-example

This flow shows that each time we append an event, Marten applies the changes immediately and updates the document inside the same transaction. This ensures strong consistency between the event stream and the projected view.

Note that this is not the same as aggregating a domain model like `FreightShipment` using `AggregateStreamAsync<T>`. Instead, we're producing a derived view (or read model) designed for fast queries, based on a subset of event data.

::: info
You can access the [FreightShipping tutorial source code](https://github.com/JasperFx/marten/tree/cfff44de42b099f4a795dbb240c53fc4d2cb1a95/docs/src/samples/FreightShipping) on GitHub.
:::
