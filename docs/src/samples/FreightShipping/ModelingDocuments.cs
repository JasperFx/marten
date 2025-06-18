using JasperFx;
using Marten;

namespace FreightShipping;

public static class ModelingDocuments
{
    public static async Task Run()
    {
        var connectionString = Utils.GetConnectionString();
        #region store-setup
        var store = DocumentStore.For(opts =>
        {
            opts.Connection(connectionString);
            opts.AutoCreateSchemaObjects = AutoCreate.All; // Dev mode: create tables if missing

            #region indexing-fields
            opts.Schema.For<Shipment>().Duplicate(x => x.Status);
            opts.Schema.For<Shipment>().Duplicate(x => x.AssignedDriverId);
            #endregion indexing-fields
        });
        #endregion store-setup
        
        #region storing-documents
        var driver = new Driver
        {
            Id = Guid.NewGuid(),
            Name = "Alice Smith",
            LicenseNumber = "A123456"
        };
        
        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            Origin = "New York",
            Destination = "Chicago",
            CreatedAt = DateTime.UtcNow,
            AssignedDriverId = driver.Id,
            Status = "Created"
        };

        await using var session = store.LightweightSession();
        session.Store(driver);
        session.Store(shipment);
        await session.SaveChangesAsync();
        #endregion storing-documents
        
        #region querying-documents
        await using var querySession = store.QuerySession();

        // Load by Id
        var existingShipment = await querySession.LoadAsync<Shipment>(shipment.Id);
        Console.WriteLine($"Loaded shipment {existingShipment!.Id} with status {existingShipment.Status}");

        // Filter by destination
        var shipmentsToChicago = await querySession
            .Query<Shipment>()
            .Where(x => x.Destination == "Chicago")
            .ToListAsync();
        Console.WriteLine($"Found {shipmentsToChicago.Count} shipments to Chicago");

        // Count active shipments per driver
        var active = await querySession
            .Query<Shipment>()
            .CountAsync(x => x.AssignedDriverId == driver.Id && x.Status != "Delivered");
        Console.WriteLine($"Driver {driver.Name} has {active} active shipments");
        #endregion querying-documents
    }
}

#region models
public class Shipment
{
    public Guid Id { get; set; }
    public string Origin { get; set; }
    public string Destination { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string Status { get; set; }
    public Guid? AssignedDriverId { get; set; }
}

public class Driver
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string LicenseNumber { get; set; }
}
#endregion models