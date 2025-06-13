using FreightShipping.EventSourcedAggregate;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;

namespace FreightShipping;

using Marten.Events.Projections;

public class CrossAggregateViews
{
    public async Task Run()
    {
        #region store-setup
        var store = DocumentStore.For(opts =>
        {
            opts.Connection("Host=localhost;Database=myapp;Username=myuser;Password=mypwd");
            opts.AutoCreateSchemaObjects = AutoCreate.All; // Dev mode: create tables if missing
            
            #region projection-setup
            opts.Projections.Add<DailyShipmentsProjection>(ProjectionLifecycle.Async);
            #endregion projection-setup
        });
        #endregion store-setup
        
        #region query-daily-deliveries
        await using var session = store.LightweightSession();
        var lastWeek = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
        var stats = await session.Query<DailyShipmentsDelivered>()
            .Where(x => x.Id >= lastWeek)
            .OrderBy(x => x.Id)
            .ToListAsync();

        foreach(var dayStat in stats)
        {
            Console.WriteLine($"{dayStat.Id}: {dayStat.DeliveredCount} deliveries");
        }
        #endregion query-daily-deliveries
    }
}

#region view-doc
public class DailyShipmentsDelivered
{
    public DateOnly Id { get; set; }        // using DateOnly as the document Id (the day)
    public int DeliveredCount { get; set; }
}
#endregion view-doc

#region daily-shipment-projection
public class DailyShipmentsProjection : MultiStreamProjection<DailyShipmentsDelivered, DateOnly>
{
    public DailyShipmentsProjection()
    {
        // Group events by the DateOnly key (extracted from DeliveredAt)
        Identity<ShipmentDelivered>(e => DateOnly.FromDateTime(e.DeliveredAt));
    }

    public DailyShipmentsDelivered Create(ShipmentDelivered @event)
    {
        // Create a new view for the date if none exists
        return new DailyShipmentsDelivered 
        {
            Id = DateOnly.FromDateTime(@event.DeliveredAt),
            DeliveredCount = 1
        };
    }

    public void Apply(ShipmentDelivered @event, DailyShipmentsDelivered view)
    {
        // Increment the count for this date
        view.DeliveredCount += 1;
    }
}
#endregion daily-shipment-projection