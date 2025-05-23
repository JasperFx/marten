# Part 1: Freight Shipping Use Case – Document-First Approach

To ground this tutorial, imagine we’re building a simple freight shipping system. In this domain, a **Shipment** has an origin and destination, and goes through a lifecycle (scheduled, picked up, in transit, delivered, etc.). We’ll begin by modeling a Shipment as a straightforward document – essentially a record in a database that we update as the shipment progresses. Marten’s document database features make this easy and familiar.

## Defining the Shipment Document

First, let’s define a Shipment class to represent the data we want to store. We’ll include an Id (as a Guid), origin and destination locations, a status, and timestamps for key events. For now, we’ll treat this as a simple POCO that Marten can persist as JSON:

```csharp
public enum ShipmentStatus { Scheduled, InTransit, Delivered, Cancelled }

public class FreightShipment
{
    public Guid Id { get; set; }
    public string Origin { get; set; }
    public string Destination { get; set; }
    public ShipmentStatus Status { get; set; }
    public DateTime ScheduledAt { get; set; }
    public DateTime? PickedUpAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}
```

Here, **Id** will uniquely identify the shipment (Marten uses this as the document identity). We track the shipment’s Origin and Destination, and use a Status to reflect where it is in the process. We also have optional timestamps for when the shipment was picked up, delivered, or cancelled (those will remain null until those actions happen). This is a **document-first model** – all the current state of a shipment is kept in one document, and we’ll overwrite fields as things change.

## Storing and Retrieving Documents with Marten

Marten makes it straightforward to work with documents. We start by configuring a `DocumentStore` with a connection to PostgreSQL. For example:

```csharp
var store = DocumentStore.For(opts =>
{
    opts.Connection("Host=localhost;Database=myapp;Username=myuser;Password=mypwd");
    opts.AutoCreateSchemaObjects = AutoCreate.All; // Dev mode: create tables if missing
});
```

The `DocumentStore` is the main entry point to Marten. In the above setup, we provide the database connection string. We also enable `AutoCreateSchemaObjects` so Marten will automatically create the necessary tables (in a real app you might use a migration instead, but this is convenient for development). Marten will create a table to hold `FreightShipment` documents in JSON form.

Now, let’s store a new shipment document and then load it back:

```csharp
using var session = store.LightweightSession();  // open a new session

// 1. Create a new shipment
var shipment = new FreightShipment 
{
    Id = Guid.NewGuid(),
    Origin = "Rotterdam",
    Destination = "New York",
    Status = ShipmentStatus.Scheduled,
    ScheduledAt = DateTime.UtcNow
};

// 2. Store it in Marten
session.Store(shipment);
await session.SaveChangesAsync();   // saves the changes to the database

// 3. Later... load the shipment by Id
var loaded = await session.LoadAsync<FreightShipment>(shipment.Id);
Console.WriteLine($"Shipment status: {loaded.Status}");  // Outputs: Scheduled
```

A few things to note in this code:

- We use a **session** (`LightweightSession`) to interact with the database. This pattern is similar to an EF Core DbContext or a NHibernate session. The session is a unit of work; we save changes at the end (which wraps everything in a DB transaction).
- Calling `Store(shipment)` tells Marten to stage that document for saving. `SaveChangesAsync()` actually commits it to PostgreSQL.
- After saving, we can retrieve the document by Id using `LoadAsync<T>`. Marten deserializes the JSON back into our `FreightShipment` object.

Behind the scenes, Marten stored the shipment as a JSON document in a Postgres table. Thanks to Marten’s use of PostgreSQL, this was an ACID transaction – if we had multiple documents or operations in the session, they’d all commit or rollback together. At this point, our shipment record might look like:

```json
{
  "Id": "3a1f...d45", 
  "Origin": "Rotterdam",
  "Destination": "New York",
  "Status": "Scheduled",
  "ScheduledAt": "2025-03-21T08:30:00Z",
  "PickedUpAt": null,
  "DeliveredAt": null,
  "CancelledAt": null
}
```

As the shipment goes through its lifecycle, we would update this document. For example, when the freight is picked up, we might do:

```csharp
loaded.Status = ShipmentStatus.InTransit;
loaded.PickedUpAt = DateTime.UtcNow;
session.Store(loaded);
await session.SaveChangesAsync();
```

This will update the existing JSON document in place (Marten knows it’s an update because the Id matches an existing document). Similarly, upon delivery, we’d set `Status = Delivered` and set `DeliveredAt`. This **state-oriented approach** is simple and works well for many cases – we always have the latest status easily available by loading the document.

However, one drawback of the document-only approach is that we lose the historical changes. Each update overwrites the previous state. If we later want to know *when* a shipment was picked up or delivered, we have those timestamps, but what if we need more detail or want an audit trail? We might log or archive old versions, but that gets complex. This is where **event sourcing** comes in. Instead of just storing the final state, we capture each state change as an event. Let’s see how Marten allows us to evolve our design to an event-sourced model without abandoning the benefits of the document store.
