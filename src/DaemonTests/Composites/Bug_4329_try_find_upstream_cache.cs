using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Composites;

public record OrderPlaced(Guid CustomerId, decimal Total);
public record OrderShipped(string Carrier);

public class Order
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Total { get; set; }
    public bool IsShipped { get; set; }
}

public class OrderShippingNotification
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public decimal OrderTotal { get; set; }
    public string Carrier { get; set; } = "";
}

public class OrderProjection: SingleStreamProjection<Order, Guid>
{
    public OrderProjection()
    {
        Options.CacheLimitPerTenant = 1000;
    }

    public override Order Evolve(Order snapshot, Guid id, IEvent e)
    {
        switch (e.Data)
        {
            case OrderPlaced placed:
                snapshot = new Order
                {
                    Id = id,
                    CustomerId = placed.CustomerId,
                    Total = placed.Total
                };
                break;
            case OrderShipped:
                snapshot.IsShipped = true;
                break;
        }

        return snapshot;
    }
}

public class OrderShippingNotificationProjection: MultiStreamProjection<OrderShippingNotification, Guid>
{
    public OrderShippingNotificationProjection()
    {
        Identity<IEvent<OrderShipped>>(e => e.StreamId);
    }

    #region sample_try_find_upstream_cache
    public override Task EnrichEventsAsync(SliceGroup<OrderShippingNotification, Guid> group,
        IQuerySession querySession, CancellationToken cancellation)
    {
        // Ask the upstream OrderProjection (running earlier in the same composite stage)
        // for its in-memory aggregate cache. A SQL query for Order in this same batch
        // would return nothing — those writes are still queued on the shared
        // IProjectionBatch and have not been committed to PostgreSQL yet.
        if (!group.TryFindUpstreamCache<Guid, Order>(out var upstreamOrders))
        {
            // No upstream stage in this composite is producing Order documents.
            return Task.CompletedTask;
        }

        foreach (var slice in group.Slices)
        {
            if (upstreamOrders.TryFind(slice.Id, out var order))
            {
                // Stamp a synthetic References<Order> event onto the slice so that
                // the Evolve method can read the upstream entity's data.
                slice.Reference(order);
            }
        }

        return Task.CompletedTask;
    }
    #endregion

    public override OrderShippingNotification Evolve(OrderShippingNotification snapshot, Guid id, IEvent e)
    {
        switch (e.Data)
        {
            case OrderShipped shipped:
                snapshot ??= new OrderShippingNotification { Id = id };
                snapshot.Carrier = shipped.Carrier;
                break;

            case References<Order> orderRef:
                snapshot ??= new OrderShippingNotification { Id = id };
                snapshot.CustomerId = orderRef.Entity.CustomerId;
                snapshot.OrderTotal = orderRef.Entity.Total;
                break;
        }

        return snapshot;
    }
}

public class Bug_4329_try_find_upstream_cache: BugIntegrationContext
{
    [Fact]
    public async Task downstream_stage_can_read_upstream_in_flight_order_via_upstream_cache()
    {
        StoreOptions(opts =>
        {
            opts.Projections.CompositeProjectionFor("OrderComposite", projection =>
            {
                projection.Add<OrderProjection>();                          // stage 1
                projection.Add<OrderShippingNotificationProjection>(2);     // stage 2
            });
        });

        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        // Both events arrive in a single batch so the Order document is being
        // produced and the OrderShipping notification is being computed in the
        // same composite execution. This is the regression scenario from #4329.
        theSession.Events.StartStream<Order>(orderId,
            new OrderPlaced(customerId, 99.95m),
            new OrderShipped("UPS"));
        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(30.Seconds());

        var notification = await theSession.LoadAsync<OrderShippingNotification>(orderId);
        notification.ShouldNotBeNull();
        notification.CustomerId.ShouldBe(customerId);
        notification.OrderTotal.ShouldBe(99.95m);
        notification.Carrier.ShouldBe("UPS");
    }
}
