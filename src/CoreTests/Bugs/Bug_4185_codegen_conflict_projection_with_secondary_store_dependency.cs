using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace CoreTests.Bugs;

public interface IBug4185Store : IDocumentStore { }
public interface IBug4185OtherStore : IDocumentStore { }

public record OrderPlaced4185(string ProductName, decimal UnitPrice, int Quantity);
public record OrderShipped4185(DateTime ShippedAt);

public class OrderSummary4185
{
    public Guid Id { get; set; }
    public string ProductName { get; set; }
    public decimal Total { get; set; }
    public bool Shipped { get; set; }
}

/// <summary>
/// Projection on the primary store that directly injects a secondary store.
/// This is the pattern that caused #4185's original codegen conflict.
/// </summary>
public class OrderProjection4185 : SingleStreamProjection<OrderSummary4185, Guid>
{
    private readonly IBug4185Store _secondaryStore;

    public OrderProjection4185(IBug4185Store secondaryStore)
    {
        _secondaryStore = secondaryStore;
    }

    public OrderSummary4185 Create(OrderPlaced4185 e)
    {
        return new OrderSummary4185
        {
            ProductName = e.ProductName,
            Total = e.UnitPrice * e.Quantity
        };
    }

    public void Apply(OrderShipped4185 e, OrderSummary4185 summary)
    {
        summary.Shipped = true;
    }
}

/// <summary>
/// Original report: https://github.com/JasperFx/marten/issues/4185
///
/// Pre-9.0 the bug was a codegen-write collision between a secondary store's
/// emitted store-implementation class and the primary's. The codegen-write
/// pipeline (and the entire <c>ICodeFileCollection</c> contract on secondary
/// stores) was retired in #4454, so the duplicate-file scenario can no longer
/// occur — see <c>SecondaryStoreProxyFactory</c> which builds the secondary-
/// store proxy via <see cref="System.Reflection.Emit"/> instead. What still
/// matters is the runtime behavior of an inline projection that takes a
/// secondary <c>IDocumentStore</c> via constructor injection; that's what
/// this remaining test pins.
/// </summary>
public class Bug_4185_codegen_conflict_projection_with_secondary_store_dependency
{
    [Fact]
    public async Task projection_with_secondary_store_dependency_should_work_at_runtime()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMartenStore<IBug4185Store>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "bug4185_sec";
                });

                services.AddMarten(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = "bug4185_pri";
                    })
                    .AddProjectionWithServices<OrderProjection4185>(
                        ProjectionLifecycle.Inline,
                        ServiceLifetime.Singleton)
                    .ApplyAllDatabaseChangesOnStartup();
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();
        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(streamId, new OrderPlaced4185("Widget", 9.99m, 3));
            await session.SaveChangesAsync();
        }

        await using (var session = store.QuerySession())
        {
            var summary = await session.LoadAsync<OrderSummary4185>(streamId);
            summary.ShouldNotBeNull();
            summary.ProductName.ShouldBe("Widget");
            summary.Total.ShouldBe(29.97m);
        }
    }
}
