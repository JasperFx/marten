using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace EventSourcingTests.Examples;

#region sample_Order_events_for_optimized_command_handling

public record OrderShipped;
public record OrderCreated(Item[] Items);
public record OrderReady;

public record ItemReady(string Name);

#endregion

#region sample_Order_for_optimized_command_handling

public class Item
{
    public string Name { get; set; }
    public bool Ready { get; set; }
}

public class Order
{
    // This would be the stream id
    public Guid Id { get; set; }

    // This is important, by Marten convention this would
    // be the
    public int Version { get; set; }

    public Order(OrderCreated created)
    {
        foreach (var item in created.Items)
        {
            Items[item.Name] = item;
        }
    }

    public void Apply(IEvent<OrderShipped> shipped) => Shipped = shipped.Timestamp;
    public void Apply(ItemReady ready) => Items[ready.Name].Ready = true;

    public DateTimeOffset? Shipped { get; private set; }

    public Dictionary<string, Item> Items { get; set; } = new();

    public bool IsReadyToShip()
    {
        return Shipped == null && Items.Values.All(x => x.Ready);
    }
}

#endregion

public record MarkItemReady(Guid OrderId, string ItemName, int Version);

public class ShipOrderHandler
{

    #region sample_fetch_for_writing_naive

    public async Task Handle1(MarkItemReady command, IDocumentSession session)
    {
        // Fetch the current value of the Order aggregate
        var stream = await session
            .Events
            .FetchForWriting<Order>(command.OrderId);

        var order = stream.Aggregate;

        if (order.Items.TryGetValue(command.ItemName, out var item))
        {
            // Mark that the this item is ready
            stream.AppendOne(new ItemReady(command.ItemName));
        }
        else
        {
            // Some crude validation
            throw new InvalidOperationException($"Item {command.ItemName} does not exist in this order");
        }

        // If the order is ready to ship, also emit an OrderReady event
        if (order.IsReadyToShip())
        {
            stream.AppendOne(new OrderReady());
        }

        await session.SaveChangesAsync();
    }

    #endregion

    #region sample_fetch_for_writing_explicit_optimistic_concurrency

    public async Task Handle2(MarkItemReady command, IDocumentSession session)
    {
        // Fetch the current value of the Order aggregate
        var stream = await session
            .Events

            // Explicitly tell Marten the exptected, starting version of the
            // event stream
            .FetchForWriting<Order>(command.OrderId, command.Version);

        var order = stream.Aggregate;

        if (order.Items.TryGetValue(command.ItemName, out var item))
        {
            // Mark that the this item is ready
            stream.AppendOne(new ItemReady(command.ItemName));
        }
        else
        {
            // Some crude validation
            throw new InvalidOperationException($"Item {command.ItemName} does not exist in this order");
        }

        // If the order is ready to ship, also emit an OrderReady event
        if (order.IsReadyToShip())
        {
            stream.AppendOne(new OrderReady());
        }

        await session.SaveChangesAsync();
    }

    #endregion

    #region sample_sample_fetch_for_writing_exclusive_lock

    public async Task Handle3(MarkItemReady command, IDocumentSession session)
    {
        // Fetch the current value of the Order aggregate
        var stream = await session
            .Events

            // Explicitly tell Marten the exptected, starting version of the
            // event stream
            .FetchForExclusiveWriting<Order>(command.OrderId);

        var order = stream.Aggregate;

        if (order.Items.TryGetValue(command.ItemName, out var item))
        {
            // Mark that the this item is ready
            stream.AppendOne(new ItemReady(command.ItemName));
        }
        else
        {
            // Some crude validation
            throw new InvalidOperationException($"Item {command.ItemName} does not exist in this order");
        }

        // If the order is ready to ship, also emit an OrderReady event
        if (order.IsReadyToShip())
        {
            stream.AppendOne(new OrderReady());
        }

        await session.SaveChangesAsync();
    }

    #endregion

    #region sample_using_WriteToAggregate

    public Task Handle4(MarkItemReady command, IDocumentSession session)
    {
        return session.Events.WriteToAggregate<Order>(command.OrderId, command.Version, stream =>
        {
            var order = stream.Aggregate;

            if (order.Items.TryGetValue(command.ItemName, out var item))
            {
                // Mark that the this item is ready
                stream.AppendOne(new ItemReady(command.ItemName));
            }
            else
            {
                // Some crude validation
                throw new InvalidOperationException($"Item {command.ItemName} does not exist in this order");
            }

            // If the order is ready to ship, also emit an OrderReady event
            if (order.IsReadyToShip())
            {
                stream.AppendOne(new OrderReady());
            }
        });
    }

    #endregion
}

public static class BootstrappingSample
{
    public static async Task bootstrap()
    {
        #region sample_registering_Order_as_Inline

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection("some connection string");

            // The Order aggregate is updated Inline inside the
            // same transaction as the events being appended
            opts.Projections.Snapshot<Order>(SnapshotLifecycle.Inline);

            // Opt into an optimization for the inline aggregates
            // used with FetchForWriting()
            opts.Projections.UseIdentityMapForInlineAggregates = true;
        })

        // This is also a performance optimization in Marten to disable the
        // identity map tracking overall in Marten sessions if you don't
        // need that tracking at runtime
        .UseLightweightSessions();

        #endregion
    }
}
