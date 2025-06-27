using JasperFx;
using Marten;

namespace FreightShipping;

public static class EvolveToEventSourcing
{
    public static async Task Run()
    {
        var connectionString = Utils.GetConnectionString();
        #region store-setup
        var store = DocumentStore.For(opts =>
        {
            opts.Connection(connectionString!);
            opts.AutoCreateSchemaObjects = AutoCreate.All; // Dev mode: create tables if missing
        });
        #endregion store-setup
        
        #region storing-events
        using var session = store.LightweightSession();

        // 1. Start a new event stream for a shipment
        var shipmentId = Guid.NewGuid();
        var scheduleEvent = new ShipmentScheduled(shipmentId, "Rotterdam", "New York", DateTime.UtcNow);
        session.Events.StartStream<FreightShipment>(shipmentId, scheduleEvent);
        await session.SaveChangesAsync();
        Console.WriteLine($"Started stream {shipmentId} with ShipmentScheduled.");

        // 2. Append a ShipmentPickedUp event (in a real scenario, later in time)
        var pickupEvent = new ShipmentPickedUp(DateTime.UtcNow.AddHours(5));
        session.Events.Append(shipmentId, pickupEvent);

        // 3. Append a ShipmentDelivered event
        var deliveredEvent = new ShipmentDelivered(DateTime.UtcNow.AddDays(1));
        session.Events.Append(shipmentId, deliveredEvent);

        // 4. Commit the new events
        await session.SaveChangesAsync();
        Console.WriteLine($"Appended PickedUp and Delivered events to stream {shipmentId}.");
        #endregion storing-events
    }
}


#region identify-events
public record ShipmentScheduled(Guid ShipmentId, string Origin, string Destination, DateTime ScheduledAt);
public record ShipmentPickedUp(DateTime PickedUpAt);
public record ShipmentDelivered(DateTime DeliveredAt);
public record ShipmentCancelled(string Reason, DateTime CancelledAt);
#endregion identify-events