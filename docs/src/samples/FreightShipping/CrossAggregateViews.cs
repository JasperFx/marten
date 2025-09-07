using JasperFx;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.Extensions.Hosting;

namespace FreightShipping;

using Marten.Events.Projections;



public static class CrossAggregateViews
{
    public static async Task RunDaemon(CancellationToken cancellationToken)
    {
        var connectionString = Utils.GetConnectionString();
        #region async-daemon-setup
        await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                    {
                        opts.Connection(connectionString!);
                        opts.AutoCreateSchemaObjects = AutoCreate.All; // Dev mode: create tables if missing
                        opts.Projections.Add<DailyShipmentsProjection>(ProjectionLifecycle.Async);
                    })
                    // Turn on the async daemon in "Solo" mode
                    // there are other modes, but this is the simplest
                    .AddAsyncDaemon(DaemonMode.Solo);
            })
            .StartAsync(cancellationToken);
        #endregion async-daemon-setup

        await Task.Delay(Timeout.Infinite, cancellationToken); // keep alive
    }

    public static async Task Run()
    {
        var connectionString = Utils.GetConnectionString();
        #region store-setup
        var store = DocumentStore.For(opts =>
        {
            opts.Connection(connectionString!);
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
            .Where(x => x.DeliveredDate >= lastWeek)
            .OrderBy(x => x.Id)
            .ToListAsync();
        Console.WriteLine(stats.Count);

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
    public string Id { get; set; } = null!;
    public DateOnly DeliveredDate { get; set; }
    public int DeliveredCount { get; set; }
}
#endregion view-doc

#region daily-shipment-projection
public class DailyShipmentsProjection : MultiStreamProjection<DailyShipmentsDelivered, string>
{
    public DailyShipmentsProjection()
    {
        // Group events by the DateOnly key as string (extracted from DeliveredAt)
        Identity<ShipmentDelivered>(e => e.DeliveredAt.ToString("yyyy-MM-dd"));
    }

    public DailyShipmentsDelivered Create(ShipmentDelivered @event)
    {
        // Create a new view for the date if none exists
        return new DailyShipmentsDelivered 
        {
            Id = @event.DeliveredAt.ToString("yyyy-MM-dd"),
            DeliveredDate = DateOnly.FromDateTime(@event.DeliveredAt),
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