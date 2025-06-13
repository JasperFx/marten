using JasperFx;
using Marten;

namespace FreightShipping;

public static class GettingStarted
{
    public static async Task Run()
    {
        #region store-setup
        var store = DocumentStore.For(opts =>
        {
            opts.Connection("Host=localhost;Database=myapp;Username=myuser;Password=mypwd");
            opts.AutoCreateSchemaObjects = AutoCreate.All; // Dev mode: create tables if missing
        });
        #endregion store-setup

        #region create-shipment-doc
        await using var session = store.LightweightSession();  // open a new session

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
        #endregion create-shipment-doc

        #region update-shipment-doc
        loaded.Status = ShipmentStatus.InTransit;
        loaded.PickedUpAt = DateTime.UtcNow;
        session.Store(loaded);
        await session.SaveChangesAsync();
        #endregion update-shipment-doc
    }
}

#region models
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
#endregion models


