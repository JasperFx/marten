using FreightShipping.EventSourcedAggregate;
using JasperFx;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Marten;

namespace FreightShipping;

public static class WolverineIntegration
{
    public static async Task RunDaemon(CancellationToken cancellationToken)
    {
        var connectionString = Utils.GetConnectionString();
        
        var builder = Host.CreateDefaultBuilder();
        
        #region wolverine-integration
        await builder
            // Wolverine generates its handler code at runtime in Development. In any other environment,
            // Wolverine 7 expects that code pre-generated with `dotnet run -- codegen write`
            .UseEnvironment(Environments.Development)

            // Wolverine itself: message handling, including the ShipmentHandler below
            .UseWolverine()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                    {
                        opts.Connection(connectionString!);
                        opts.AutoCreateSchemaObjects = AutoCreate.All; // Dev mode: create tables if missing
                        opts.Projections.Add<DailyShipmentsProjection>(ProjectionLifecycle.Async);
                        opts.Projections.Add<ShipmentViewProjection>(ProjectionLifecycle.Async);
                    })
                    // With UseWolverineManagedEventSubscriptionDistribution, Wolverine runs the async projections,
                    // so there is no AddAsyncDaemon() call here
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
