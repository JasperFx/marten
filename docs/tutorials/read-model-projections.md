# Part 4: Projections – Building Read Models from Events

In event sourcing, events are the source of truth, but they’re not always convenient for querying or presenting to users. **Projections** are derived views or read models built from those events ([Marten as Event Store | Marten](https://martendb.io/events/#:~:text=Marten%27s%20Event%20Store%20functionality%20is,its%20rich%20support%20for%20projections)). We already saw one kind of projection: an aggregate projection that builds the current `FreightShipment` state. Marten’s projection system is quite powerful – it allows you to project events into virtually any shape of data: aggregated documents, view tables, or multiple related documents.

Let’s discuss a few projection types and best practices, continuing with our freight shipment domain as context.

## Inline vs. Async Projections

Marten supports three projection lifecycles: **Inline**, **Async**, and **Live** ([Marten as Event Store | Marten](/events/)). We have touched on these, but here’s a quick comparison:

- **Inline projections** run as part of the same transaction that records the events. This yields **strong consistency** (the projection is updated immediately, within the ACID transaction). The trade-off is that it can add latency to the write operation. In our example, updating the `FreightShipment` document inline ensures any query immediately after the event commit will see the new state.
- **Async projections** run in the background, typically via Marten’s Projection Daemon or with the help of Wolverine (more on that soon). When events are committed, they are queued for processing and a separate process (or thread) will update the projections shortly after. This is an **eventual consistency** model, but it can vastly improve write throughput, since the event insert transaction doesn’t do extra work. For heavy workloads, this is a common choice – you accept that there may be a tiny delay before the read models reflect the latest events.
- **Live projections** are on-demand and not persisted. We saw an example using `AggregateStreamAsync<T>`. Another scenario for live projections might be a complex aggregation you only need once (like generating a report on the fly by scanning events). Marten’s `QueryAllRawEvents().AggregateToAsync<T>()` API allows you to apply a projection dynamically to any event query ([Live Aggregation | Marten](https://martendb.io/events/projections/live-aggregates#:~:text=cs)). Live projections are essentially **ad hoc** computations and do not maintain state beyond the immediate query.

In practice, you might use a mix: aggregates that are needed in real-time might be inline, whereas other read models might be async. Marten makes it easy to register different projections with different lifecycles.

For our freight system, suppose we want to generate a **shipment timeline** view (list of events with timestamps for a shipment) whenever needed. We might simply fetch the events and not store that as a document – this can be a live projection (just materialize events to a DTO when needed). Meanwhile, the `FreightShipment` current status we chose to maintain inline for instant consistency.

## Designing Projections and Naming Conventions

When building projections, especially multi-step ones, it’s good to follow clear naming and separation:
- Keep your event classes in a domain namespace (they represent business facts).
- The aggregate (like `FreightShipment`) lives in the domain as well, with the apply methods as we did.
- If you create separate projection classes (as we will for multi-stream projections soon), name them after the view or purpose (e.g., `DailyShipmentsProjection` for a daily summary). This keeps things organized.
- Each projection should focus on one concern: an aggregate projection per stream type, or a specific read model that serves a query need.

Marten will persist projection results as documents (or in user-defined tables for certain custom projections). By default, the document type name will determine the table name. For example, `FreightShipment` documents go in the `mt_doc_freightshipment` table (by Marten’s conventions). You can customize this via Marten’s schema config if needed.

Now, let’s move on to a more advanced kind of projection: combining events from **multiple streams**.