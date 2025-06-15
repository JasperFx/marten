using FreightShipping.EventSourcedAggregate;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.Extensions.Hosting;
using Wolverine.Marten;

namespace FreightShipping;

public static class WolverineIntegration
{
    public static void Run()
    {
        var builder = Host.CreateApplicationBuilder();
        
        #region wolverine-integration
        builder.Services.AddMarten(opts =>
        {
            opts.Connection("Host=localhost;Database=myapp;Username=myuser;Password=mypwd");
            opts.Projections.Add<DailyShipmentsProjection>(ProjectionLifecycle.Async);
            opts.Projections.Add<ShipmentViewProjection>(ProjectionLifecycle.Async);
        })
        .IntegrateWithWolverine(cfg =>
        {
            cfg.UseWolverineManagedEventSubscriptionDistribution = true;
        });
        #endregion wolverine-integration
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
