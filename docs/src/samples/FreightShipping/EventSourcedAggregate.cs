using JasperFx;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;

namespace FreightShipping.EventSourcedAggregate;

public static class EventSourcedAggregate
{
    public static async Task Run()
    {
        var connectionString = Utils.GetConnectionString();
        #region store-setup
        var store = DocumentStore.For(opts =>
        {
            opts.Connection(connectionString);
            opts.AutoCreateSchemaObjects = AutoCreate.All; // Dev mode: create tables if missing
            opts.Projections.Add<ShipmentViewProjection>(ProjectionLifecycle.Inline);
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
        
        #region live-aggregate
        // Assuming we have a stream of events for shipmentId (from earlier Part)
        var currentState = await session.Events.AggregateStreamAsync<FreightShipment>(shipmentId);
        Console.WriteLine($"State: {currentState!.Status}, PickedUpAt: {currentState.PickedUpAt}");
        #endregion live-aggregate
        
        #region shipment-example
        await using var session2 = store.LightweightSession();

        var sid = Guid.NewGuid();
        var evt1 = new ShipmentScheduled(sid, "Los Angeles", "Tokyo", DateTime.UtcNow);
        session2.Events.StartStream<ShipmentView>(sid, evt1);
        await session.SaveChangesAsync();  // Inserts initial ShipmentView

        var evt2 = new ShipmentPickedUp(DateTime.UtcNow.AddHours(2));
        session2.Events.Append(sid, evt2);
        await session2.SaveChangesAsync();  // Updates ShipmentView.Status and PickedUpAt

        var doc = await session2.LoadAsync<ShipmentView>(sid);
        Console.WriteLine(doc!.Status);         // InTransit
        Console.WriteLine(doc.PickedUpAt);    // Set to pickup time
        #endregion shipment-example
    }
}

#region define-aggregate
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
#endregion define-aggregate

#region single-stream-projection
public class ShipmentView
{
    public Guid Id { get; set; }
    public string Origin { get; set; }
    public string Destination { get; set; }
    public string Status { get; set; }
    public DateTime? PickedUpAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}

public class ShipmentViewProjection : SingleStreamProjection<ShipmentView, Guid>
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
#endregion single-stream-projection
