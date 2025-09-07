using FreightShipping.EventSourcedAggregate;
using JasperFx;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.Extensions.Hosting;
using Wolverine.Marten;

namespace FreightShipping;

public static class WolverineIntegration
{
    public static async Task RunDaemon(CancellationToken cancellationToken)
    {
        var connectionString = Utils.GetConnectionString();
        
        var builder = Host.CreateDefaultBuilder();
        
        #region wolverine-integration    
        await builder.ConfigureServices(services =>
        {
            services.AddMarten(opts =>
                {
                    opts.Connection(connectionString!);
                    opts.AutoCreateSchemaObjects = AutoCreate.All; // Dev mode: create tables if missing
                    opts.Projections.Add<DailyShipmentsProjection>(ProjectionLifecycle.Async);
                    opts.Projections.Add<ShipmentViewProjection>(ProjectionLifecycle.Async);
                })
                .AddAsyncDaemon(DaemonMode.HotCold)
                .IntegrateWithWolverine(cfg =>
                {
                    cfg.UseWolverineManagedEventSubscriptionDistribution = true;
                });
        })
        .StartAsync(cancellationToken);
        #endregion wolverine-integration

        await Task.Delay(Timeout.Infinite, cancellationToken); // keep alive
    }
}

public static class ShipmentHandler
{
    #region aggregate-handler
    [AggregateHandler]
    public static IEnumerable<object> Handle(PickupShipment cmd, FreightShipment shipment)
    {
        if (shipment.Status != ShipmentStatus.Scheduled)
            throw new InvalidOperationException("Cannot pick up unscheduled shipment");

        yield return new ShipmentPickedUp(cmd.Timestamp);
        yield return new NotifyDispatchCenter(shipment.Id, "PickedUp");
    }
    #endregion aggregate-handler
}

public record NotifyDispatchCenter(Guid ShipmentId, string Pickedup);
public record PickupShipment(DateTime Timestamp);
