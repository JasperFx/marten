using JasperFx;
using Marten;

namespace FreightShipping;

public static class ModelingDocuments
{
    public static async Task Run()
    {
        #region store-setup
        var store = DocumentStore.For(opts =>
        {
            opts.Connection("Host=localhost;Database=myapp;Username=myuser;Password=mypwd");
            opts.AutoCreateSchemaObjects = AutoCreate.All; // Dev mode: create tables if missing

            #region indexing-fields
            opts.Schema.For<Shipment>().Duplicate(x => x.Status);
            opts.Schema.For<Shipment>().Duplicate(x => x.AssignedDriverId);
            #endregion indexing-fields
        });
        #endregion store-setup
        
        #region storing-documents
        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            Origin = "New York",
            Destination = "Chicago",
            CreatedAt = DateTime.UtcNow,
            Status = "Created"
        };

        var driver = new Driver
        {
            Id = Guid.NewGuid(),
            Name = "Alice Smith",
            LicenseNumber = "A123456"
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

        // Filter by destination
        var shipmentsToChicago = await querySession
            .Query<Shipment>()
            .Where(x => x.Destination == "Chicago")
            .ToListAsync();

        // Count active shipments per driver
        var active = await querySession
            .Query<Shipment>()
            .CountAsync(x => x.AssignedDriverId == driver.Id && x.Status != "Delivered");
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