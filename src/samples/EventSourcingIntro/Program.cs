using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;

const string connectionString =
    "PORT = 5432; HOST = localhost; TIMEOUT = 15; POOLING = True; DATABASE = 'postgres'; PASSWORD = 'qwerty'; USER ID = 'postgres'";

var documentStore = DocumentStore.For(options =>
{
    options.Connection(connectionString);

    options.Projections.Add<WarehouseProductProjection>(ProjectionLifecycle.Inline);
});

var id = Guid.NewGuid();

var warehouseRepository = new WarehouseRepository(documentStore);
var warehouseProductReadModel = warehouseRepository.Get(id);

DemoConsole.WriteWithColour($"{warehouseProductReadModel?.QuantityOnHand ?? 0} items of stock in the warehouse for {id}");

var handler = new WarehouseProductHandler(id, documentStore);
handler.ReceiveProduct(100);

DemoConsole.WriteWithColour($"Received 100 items of stock into the warehouse for {id}");

handler.ShipProduct(10);

DemoConsole.WriteWithColour($"Shipped 10 items of stock out of the warehouse for {id}");

handler.AdjustInventory(5,"Ordered too many");

DemoConsole.WriteWithColour($"Found 5 items of stock hiding in the warehouse for {id} and have adjusted the stock count");

warehouseProductReadModel = warehouseRepository.Get(id);

DemoConsole.WriteWithColour($"{warehouseProductReadModel.QuantityOnHand} items of stock in the warehouse for {warehouseProductReadModel.Id}");


public record ProductShipped(Guid Id, int Quantity, DateTime DateTime);

public record ProductReceived(Guid Id, int Quantity, DateTime DateTime);

public record InventoryAdjusted(Guid Id, int Quantity, string Reason, DateTime DateTime);


public class WarehouseRepository
{
    private readonly IDocumentStore documentStore;

    public WarehouseRepository(IDocumentStore documentStore)
    {
        this.documentStore = documentStore;
    }

    public WarehouseProductReadModel Get(Guid id)
    {
        using var session = documentStore.QuerySession();

        var doc = session.Query<WarehouseProductReadModel>()
            .SingleOrDefault(x => x.Id == id);

        return doc;
    }
}

public class WarehouseProductReadModel
{
    public Guid Id { get; set; }
    public int QuantityOnHand { get; set; }
}

public class WarehouseProductProjection: SingleStreamAggregation<WarehouseProductReadModel>
{
    public WarehouseProductProjection()
    {
        ProjectEvent<ProductShipped>(Apply);
        ProjectEvent<ProductReceived>(Apply);
        ProjectEvent<InventoryAdjusted>(Apply);
    }


    public void Apply(WarehouseProductReadModel readModel, ProductShipped evnt)
    {
        readModel.QuantityOnHand -= evnt.Quantity;
    }

    public void Apply(WarehouseProductReadModel readModel, ProductReceived evnt)
    {
        readModel.QuantityOnHand += evnt.Quantity;
    }

    public void Apply(WarehouseProductReadModel readModel, InventoryAdjusted evnt)
    {
        readModel.QuantityOnHand += evnt.Quantity;
    }
}

public class WarehouseProductWriteModel
{
    public Guid Id { get; set; }
    public int QuantityOnHand { get; set; }

    public void Apply(ProductShipped evnt)
    {
        Id = evnt.Id;
        QuantityOnHand -= evnt.Quantity;
    }

    public void Apply(ProductReceived evnt)
    {
        Id = evnt.Id;
        QuantityOnHand += evnt.Quantity;
    }

    public void Apply(InventoryAdjusted evnt)
    {
        Id = evnt.Id;
        QuantityOnHand += evnt.Quantity;
    }
}

public class WarehouseProductHandler
{
    private readonly Guid id;
    private readonly IDocumentStore documentStore;

    public WarehouseProductHandler(Guid id, IDocumentStore documentStore)
    {
        this.id = id;
        this.documentStore = documentStore;
    }

    public void ShipProduct(int quantity)
    {
        using var session = documentStore.LightweightSession();

        var warehouseProduct = session.Events.AggregateStream<WarehouseProductWriteModel>(id);

        if (quantity > warehouseProduct?.QuantityOnHand)
        {
            throw new InvalidDomainException("Ah... we don't have enough product to ship?");
        }

        session.Events.Append(id, new ProductShipped(id, quantity, DateTime.UtcNow));
        session.SaveChanges();
    }

    public void ReceiveProduct(int quantity)
    {
        using var session = documentStore.LightweightSession();

        session.Events.Append(id, new ProductReceived(id, quantity, DateTime.UtcNow));
        session.SaveChanges();
    }

    public void AdjustInventory(int quantity, string reason)
    {
        using var session = documentStore.LightweightSession();

        var warehouseProduct = session.Events.AggregateStream<WarehouseProductWriteModel>(id);

        if (warehouseProduct?.QuantityOnHand + quantity < 0)
        {
            throw new InvalidDomainException("Cannot adjust to a negative quantity on hand.");
        }

        session.Events.Append(id, new InventoryAdjusted(id, quantity, reason, DateTime.UtcNow));
        session.SaveChanges();
    }
}

public class InvalidDomainException: Exception
{
    public InvalidDomainException(string message): base(message)
    {
    }
}

public static class DemoConsole
{
    public static void WriteWithColour(string value)
    {
        lock (Console.Out)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(value);
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}
