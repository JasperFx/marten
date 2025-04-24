# Part 7: Advanced Considerations – Optimistic Concurrency and Deployment

In this final section, we address how to maintain data consistency with optimistic concurrency and how to evolve your projections safely using blue/green deployment techniques. The focus is on using Marten’s features (like `FetchLatest<T>()` and `ProjectionVersion`) and Wolverine’s new capabilities to achieve zero downtime even as your freight delivery system grows in complexity.

## Optimistic Concurrency with Marten

Optimistic concurrency control prevents conflicts between concurrent operations by detecting collisions and aborting the transaction rather than overwriting data. In an event-sourced system like our freight and delivery application, multiple services or users might attempt to update the same aggregate (for example, the same `FreightShipment` stream) at the same time. Marten helps manage this by **optimistically** assuming each transaction will succeed, but it will **fail fast** if it detects another session has already modified the stream.

Marten’s event store uses a versioning mechanism under the covers (each event stream has a current version number). To leverage this, Marten provides the `IDocumentSession.Events.FetchLatest<T>()` API for event streams. This method fetches the current state **and** version of the aggregate in one call. For instance, in a command handler updating a shipment:

```csharp
// Fetch current state of the FreightShipment stream with concurrency check
var stream = await session.Events.FetchLatest<FreightShipment>(shipmentId);

if (stream.Aggregate == null) throw new InvalidOperationException("Shipment not found");

// ... perform domain logic, possibly append new events ...
stream.AppendOne(new FreightDispatched(...));

await session.SaveChangesAsync(); // will throw ConcurrencyException if conflict
```

In the above example, `FetchLatest<FreightShipment>(shipmentId)` retrieves the latest state of the shipment aggregate (building it from events if needed) and tags the session with the current stream version. When you call `SaveChangesAsync()`, Marten will automatically check that no other updates have occurred on that stream. If another process successfully wrote to the same `FreightShipment` after our fetch (e.g. another dispatch command on the same shipment), Marten will throw a `ConcurrencyException` on save, aborting the transaction. This ensures you never accidentally persist changes based on stale data. In practice, you would catch this exception and handle it (for example, by retrying the operation or returning an error to the caller) as appropriate for your workflow.

Why use `FetchLatest`? As explained in [the docs here](/events/projections/read-aggregates.html#fetchlatest), we strongly recommend using this pattern for any command that appends events to an existing stream ([Appending Events](https://martendb.io/events/appending.html)). By loading the aggregate’s current state and version in one go, you both simplify your command logic and gain built-in concurrency protection. Another benefit is that `FetchLatest` abstracts whether the aggregate is computed on the fly (“live” aggregation) or read from a persisted projection (“inline” or async) – your code doesn’t need to care, it just gets the latest state. The trade-off with optimistic concurrency is that a conflict causes a rollback of the transaction; however, this is usually acceptable in a domain like freight shipping where, for example, two dispatch updates to the same shipment should not both succeed. It is far better to catch the conflict and handle it than to have undetected double updates.

> **Note:** Marten also offers a more stringent **pessimistic concurrency** option via `FetchForExclusiveWriting<T>()`, which places a database lock on the stream while processing. This guarantees exclusive access but can increase latency and risk deadlocks. In most cases, the optimistic approach is sufficient and more scalable. Use exclusive writes only if you truly need a single-writer guarantee and are aware of the performance implications.

## Evolving Your Schema and Blue/Green Deployments

Over time, you will likely need to evolve your projections or aggregate schemas. Whether to fix bugs, accommodate new business requirements, or add features. The challenge is doing this **without downtime**, especially in a running system where projections are continually updated by incoming events. Marten, with help from Wolverine, provides a strategy to deploy new projection versions side-by-side with old ones, often called a **blue/green deployment** in deployment terminology.

Imagine our `DailyShipmentsProjection` (which aggregates daily freight shipment data for reporting) needs a schema change. Say we want to add a new calculated field or change how shipments are categorized. Rebuilding this projection from scratch will take time and we don’t want to take the system offline. The solution is to run a new version of the projection in parallel with the old one until the new version is fully caught up and ready to replace the old.

### Side-by-Side Projections with `ProjectionVersion`

Marten allows you to define a new projection as a *versioned* upgrade of an existing one by using the `ProjectionVersion` property on the projection definition. By incrementing the version number, you signal to Marten that this projection should be treated as a separate entity (with its own underlying storage). For example, if `DailyShipmentsProjection` was version 1, we might create an updated projection class and set `ProjectionVersion = 2`. Marten will then write the v2 projection data to new tables, independent of the v1 tables.

This versioning mechanism is the “magic sauce” that enables running two generations of a projection side by side. The old (v1) projection continues to operate on the existing data, while the new (v2) projection starts fresh, processing all historical events as if it were building from scratch. In our freight system, that means the new `DailyShipmentsProjection` will begin consuming the event stream for shipments (e.g. `FreightShipment` events) and populating its new tables from day one’s data forward. During this time, your application is still serving reads from the old projection (v1) so there’s no interruption in service. Marten effectively treats the two versions as distinct projections running in parallel.

**Important:** When using this approach, the new projection **must run in the async lifecycle** (even if the old one was inline or live). In a blue/green deployment scenario, you typically deploy the new version of the service with the projection version bumped, and configure it as an asynchronous projection. This way, the new projection will build in the background without blocking incoming commands. Marten’s `FetchLatest` will still ensure that any command processing on an aggregate (e.g. a `FreightShipment` update) can “fast-forward” the relevant part of the projection on the fly, so even strongly consistent write-side operations continue to work with the new projection version. The system might run a bit slower while the async projection catches up, but it remains available – slow is better than down.

### Isolating Projections with Wolverine

Running two versions of a projection concurrently requires careful coordination in a multi-node environment. You want the **“blue”** instances of your application (running the old projection) and the **“green”** instances (running the new projection) to each process *their own* projection version, without stepping on each other’s toes. This is where Wolverine’s capabilities come into play.

When you integrate Marten with Wolverine and enable Wolverine’s *projection distribution* feature, Wolverine can control which nodes run which projections. Specifically, Wolverine supports restricting projections (or event subscribers) to nodes that declare a certain capability ([Projection/Subscription Distribution | Wolverine](https://wolverinefx.net/guide/durability/marten/distribution.html)). In practice, this means you could deploy your new version of the application and tag those new nodes with a capability like `"DailyShipmentsV2"` (while old nodes either lack it or perhaps have `"DailyShipmentsV1"`). Wolverine’s runtime will ensure that the **DailyShipmentsProjection v2** runs only on the nodes that have the V2 capability, and likewise the v1 projection continues to run on the older nodes. This isolation is crucial: it prevents, say, an old node from trying to run the new projection (which it doesn’t know about) or a new node accidentally double-processing the old projection. Essentially, Wolverine helps orchestrate a clean cut between blue and green workloads.

Enabling this is straightforward. When configuring Marten with Wolverine, you would call `IntegrateWithWolverine(...)` and set `UseWolverineManagedEventSubscriptionDistribution = true` (as shown in earlier chapters). Then, you assign capabilities to your application nodes (via configuration or environment variables). For example, you might configure the new deployment to advertise a `"v2"` capability. Marten’s projection registration for the new version can be made conditional or simply present only in the new code. Once running, Wolverine’s leader node will distribute projection agents such that every projection-version combination is active on exactly one node in the cluster ([Projection/Subscription Distribution | Wolverine](https://wolverinefx.net/guide/durability/marten/distribution.html)) ([Projection/Subscription Distribution | Wolverine](https://wolverinefx.net/guide/durability/marten/distribution.html)). The “blue” cluster continues to process v1, and the “green” cluster processes v2.

### Blue/Green Deployment Step-by-Step

Combining Marten’s projection versioning with Wolverine’s distribution gives you a robust zero-downtime deployment strategy. At a high level, the process to evolve a projection with no downtime looks like this:

1. **Bump the projection version in code:** Update your projection class (e.g. `DailyShipmentsProjection`) to a new `ProjectionVersion`. This indicates a new schema/logic version that will use a separate set of tables.
2. **Deploy the new version (Green) alongside the old (Blue):** Start up one or more new application instances running the updated code. At this point, both the old and new versions of the service are running. The old nodes are still serving users with version 1 of the projection, while the new nodes begin operating with version 2. If using Wolverine, ensure the new nodes have the appropriate capability so they exclusively run the v2 projection ([Projection/Subscription Distribution | Wolverine](https://wolverinefx.net/guide/durability/marten/distribution.html)).
3. **Run projections in parallel:** The v2 projection starts in async mode and begins rebuilding its data from the event store. Both versions consume incoming events: blue nodes continue to update the v1 projection, and green nodes update v2. The event stream (e.g. all `FreightShipment` events) is essentially being forked into two projection outputs. Because of the version separation, there’s no conflict – v1 writes to the old tables, v2 writes to the new tables.
4. **Monitor and catch up:** Allow the new projection to catch up to near real-time. Depending on the volume of past events, this could take some time. During this phase, keep most user read traffic directed to the blue nodes (since they have the up-to-date v1 projection). The system remains fully operational; the only overhead is the background work on green nodes to build the new projection. Marten and Wolverine ensure that the new projection stays isolated while it lags behind.
5. **Cut over to the new version:** Once the v2 projection is up-to-date (or close enough), you can switch the user traffic to the green nodes. For example, update your load balancer or service discovery to route requests to the new deployment. Now the reads are coming from `DailyShipmentsProjection` v2. Because v2 has been fully built, users should see the new data (including any backfilled changes).
6. **Retire old nodes and clean up:** With traffic on the new version, you can shut down the remaining blue nodes. The old projection (v1) will stop receiving events. At this point, it’s safe to decommission the old projection’s resources. Marten does not automatically drop the old tables, so you should remove or archive them via a migration or manual SQL after confirming the new projection is stable. The system is now running entirely on the updated projection schema.

```mermaid
flowchart TD
    subgraph Old_System [Blue (Old Version)]
      A[FreightShipment events] --> |v1 projection| P1[DailyShipmentsProjection V1];
    end
    subgraph New_System [Green (New Version)]
      A --> |v2 projection| P2[DailyShipmentsProjection V2 (async rebuild)];
    end
    P2 --> |catch up| P2Done[Projection V2 up-to-date];
    P1 -. serving reads .-> Users((Users));
    P2Done -. switch reads .-> Users;
    P1 --> |decommission| X[Old projection retired];
```

In summary, Marten’s `ProjectionVersion` feature and Wolverine’s projection distribution **work in tandem** to support zero-downtime deployments for projection changes. Use `ProjectionVersion` when you need to introduce breaking changes to a projection’s shape or data – it gives you a clean slate in the database for the new logic. Use Wolverine’s capabilities to **isolate the new projection to specific nodes**, ensuring old and new versions don’t interfere . By using both strategies together, your freight system can deploy updates (like a new `DailyShipmentsProjection` schema) with minimal disruption: the new projection back-fills data while the old one handles live traffic, and a smooth cutover ensures continuity . This approach, as described in [Jeremy Miller’s 2025 write-up on zero-downtime projections](https://jeremydmiller.com/2025/03/26/projections-consistency-models-and-zero-downtime-deployments-with-the-critter-stack/), lets you evolve your event-driven system confidently without ever putting up a “service unavailable” sign.