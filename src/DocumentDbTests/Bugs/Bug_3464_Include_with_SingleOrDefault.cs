using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Xunit;
using Shouldly;

namespace DocumentDbTests.Bugs;

public class Bug_3464_Include_with_SingleOrDefault : BugIntegrationContext
{
    [Fact]
    public async Task can_make_the_query()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<OrderItem>().AddSubClass<Product>()
                .AddSubClass<Subscription>();
        });

        var product = new Product();
        theSession.Store(product);

        var user = new User3464();
        theSession.Store(user);

        var order = new Order { UserId = user.Id, OrderItemId = product.Id };
        theSession.Store(order);

        await theSession.SaveChangesAsync();

        IList<Product> orderItems = [];
        IList<User3464> users = [];

        var orderId = order.Id;

        var order2 = await theSession.Query<Order>()
            .Include(o => o.OrderItemId, orderItems)
            .Include(o => o.UserId, users)
            .SingleOrDefaultAsync(o => o.Id == orderId);

        order2.ShouldNotBeNull();
        orderItems.Single().Id.ShouldBe(product.Id);
        users.Single().Id.ShouldBe(user.Id);
    }
}

public class OrderDetails : ICompiledQuery<Order, Order?>
{
    public Guid OrderId { get; init; }

    public IList<OrderItem> OrderItems { get; } = [];
    public IList<User3464> Users { get; } = [];

    public Expression<Func<IMartenQueryable<Order>, Order?>> QueryIs()
    {
        return q => q
            .Include(o => o.OrderItemId, OrderItems)
            .Include(o => o.UserId, Users)
            .SingleOrDefault(o => o.Id == OrderId);
    }
}

public class Order
{
    public Guid Id { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid UserId { get; set; }
}

public class User3464
{
    public Guid Id { get; set; }
}

public abstract class OrderItem
{
    public Guid Id { get; set; }

}

public class Product: OrderItem
{

}

public class Subscription: OrderItem
{

}
