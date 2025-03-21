# Part 7: Advanced Considerations – Optimistic Concurrency and Deployment

Before we conclude, let’s touch on some advanced topics and best practices that ensure your event-sourced system remains reliable and easy to evolve: **optimistic concurrency** when writing events, and strategies for deploying changes (including the blue/green approach).

## Safely Writing Events with Optimistic Concurrency

When multiple processes or users might act on the same aggregate (shipment) concurrently, we need to handle the possibility of conflicting updates. For instance, if two different services attempted to append events to the same shipment stream at roughly the same time (say one records a pickup while another records a cancellation), one of those should fail or be retried to maintain consistency. Marten uses **optimistic concurrency control** on event streams by default – each event stream has a version number that Marten checks when appending new events.

A convenient API Marten provides is `FetchForWriting<T>()`. This method loads the current aggregate state (using either live aggregation or the latest projected snapshot) and prepares the session for a concurrency check on that stream ([Marten 7 makes “Write Model” Projections Super – The Shade Tree Developer](https://jeremydmiller.com/2024/03/05/marten-7-makes-write-model-projections-super/#:~:text=This%20API%20completely%20hides%20away,that%20new%20events%20are%20captured)). Let’s see how it might be used in a command handling scenario:

```csharp
// Example: handling a "PickUpShipment" command
public async Task Handle(PickUpShipment cmd)
{
    using var session = store.LightweightSession();
    var stream = await session.Events.FetchForWriting<FreightShipment>(cmd.ShipmentId);
    if (stream.Aggregate == null)
    {
        throw new InvalidOperationException("Shipment not found");
    }

    // Business rule: only allow pickup if currently Scheduled
    if (stream.Aggregate.Status != ShipmentStatus.Scheduled)
    {
        throw new InvalidOperationException($"Cannot pick up shipment in status {stream.Aggregate.Status}");
    }

    // Everything looks good, append the event
    var pickedUp = new ShipmentPickedUp(DateTime.UtcNow);
    stream.AppendOne(pickedUp);
    await session.SaveChangesAsync(); // will throw MartenConcurrencyException if conflict
}
```

What’s happening here:
- `FetchForWriting<FreightShipment>(id)` loads the shipment aggregate state for us into `stream.Aggregate`. We can then inspect the current Status or other data to decide how to handle the command. This is much like loading the document, except it’s aware that we intend to write.
- When we call `SaveChangesAsync()`, Marten will automatically enforce that no one else has added events to this stream between the time we fetched and the time of saving. If another process did append an event, Marten will detect a version mismatch and throw a `MartenConcurrencyException` (which our code can catch and handle, perhaps by retrying the operation or informing the user of a conflict).
- The beauty of `FetchForWriting` is that it hides whether the aggregate was loaded from a live aggregation or a stored snapshot; it gives us a unified way to get the state and concurrency check ([Marten 7 makes “Write Model” Projections Super – The Shade Tree Developer](https://jeremydmiller.com/2024/03/05/marten-7-makes-write-model-projections-super/)). In Marten 7, as we saw, this works even with async projections by combining the last saved aggregate and any new events since that snapshot. So it’s efficient and up-to-date.

Using optimistic concurrency means we don’t lock the database row for the whole operation; we just verify at the end that we were working with the correct version. This is usually sufficient and preferable for performance.

**Best practice:** Always handle the possibility of a concurrency exception when writing to an event stream. Even if rare, it can happen under load. You might implement a retry mechanism (with a limit) where you reload the aggregate and try to apply the command’s logic again on the new state if a conflict occurs.

## Evolving Your Schema and Blue/Green Deployments

One of the challenges in long-running systems is evolving the event and projection models. Marten, with PostgreSQL, offers some flexibility here too:
- Because events are stored as JSON, adding new fields to events or new event types doesn’t break old events. You can use techniques like default values or event upcasting (transforming old event data to new structure on the fly) if needed, but Marten will largely store whatever your .NET type was at the time. If you rename or remove properties, you may need to handle backwards compatibility.
- For projections, if you change the shape of a projected document (say you add a new property or change its type), you will likely need to rebuild that projection. Marten can rebuild projections by replaying all events. This can be done online (it’s essentially just running through the events again to produce new docs). For large datasets, that might be time-consuming, but it’s straightforward. You could also just change `ProjectionVersion` on you projection and let it create a new version of the document beside the old one as shown in [this test](https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Aggregation/blue_green_deployment_of_aggregates.cs).

When using **Wolverine’s blue/green support**, as mentioned, you could introduce a new projection and run it on new nodes. Concretely:
- Deploy new code with the new projection (and a new document type or table).
- Those nodes have a Wolverine capability flag, so they start processing events into the new projection without interfering with the old one ([Projection/Subscription Distribution | Wolverine](https://wolverinefx.net/guide/durability/marten/distribution.html)).
- You can verify the new projection’s data (since both old and new are being updated in parallel).
- Once satisfied, switch your application to read from the new projection (i.e., update queries or APIs to use the new document type).
- Then you can undeploy the old projection/nodes.

This approach avoids taking the system down to rebuild a projection, at the cost of some temporary duplicate processing. Wolverine’s coordination ensures each event is processed by both versions safely.

For simpler changes, you might not need all that ceremony – for example, adding a field that can be backfilled lazily might just require a partial rebuild or default handling in code.