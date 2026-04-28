using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

// Marker interface for the ancillary store that holds reference products
public interface IProductStore : IDocumentStore;

public class ancillary_store_enrichment_tests : IAsyncLifetime
{
    private IHost _host = null!;
    private IDocumentStore _primaryStore = null!;

    public async Task InitializeAsync()
    {
        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ancillary_enrich_primary";
                })
                .AddProjectionWithServices<OrderProjection>(ProjectionLifecycle.Async, ServiceLifetime.Singleton)
                .ApplyAllDatabaseChangesOnStartup();

                services.AddMartenStore<IProductStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ancillary_enrich_products";
                })
                .ApplyAllDatabaseChangesOnStartup();
            })
            .StartAsync();

        _primaryStore = _host.Services.GetRequiredService<IDocumentStore>();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task enrichment_from_ancillary_store_resolves_entity_and_maps_to_projection()
    {
        var productStore = _host.Services.GetRequiredService<IProductStore>();

        // Seed a product in the ancillary store
        var productId = Guid.NewGuid();
        await using (var session = productStore.LightweightSession())
        {
            session.Store(new Product { Id = productId, Name = "Widget Pro" });
            await session.SaveChangesAsync();
        }

        // Append an event in the primary store that references the product
        var streamId = Guid.NewGuid();
        await using (var session = _primaryStore.LightweightSession())
        {
            session.Events.StartStream<Order>(streamId, new OrderPlaced { ProductId = productId });
            await session.SaveChangesAsync();
        }

        using var daemon = await _primaryStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(15.Seconds());
        await daemon.StopAllAsync();

        await using var query = _primaryStore.QuerySession();
        var order = await query.LoadAsync<Order>(streamId);

        order.ShouldNotBeNull();
        order.ProductName.ShouldBe("Widget Pro");
    }
}

// ── domain model ──────────────────────────────────────────────────────────────

public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class Order
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
}

public record OrderPlaced
{
    public Guid ProductId { get; init; }
    public Product? Product { get; set; }
}

// ── projection ────────────────────────────────────────────────────────────────

public class OrderProjection : SingleStreamProjection<Order, Guid>
{
    private readonly Lazy<IProductStore> _productStore;

    public OrderProjection(Lazy<IProductStore> productStore)
    {
        _productStore = productStore;
    }

    public override async Task EnrichEventsAsync(
        SliceGroup<Order, Guid> group,
        IQuerySession querySession,
        CancellationToken cancellation)
    {
        await group.EnrichWith<Product>()
            .UsingStore(_productStore)
            .ForEvent<OrderPlaced>()
            .ForEntityId(e => e.ProductId)
            .EnrichAsync((slice, e, product) =>
            {
                e.Data.Product = product;
            });
    }

    public Order Apply(IEvent<OrderPlaced> e, Order order)
    {
        order.ProductId = e.Data.ProductId;
        order.ProductName = e.Data.Product?.Name ?? string.Empty;
        return order;
    }
}
