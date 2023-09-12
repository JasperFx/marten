using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Shouldly;

namespace EventSourcingTests.Projections;


public class projections_with_IoC_services
{
    [Fact]
    public async Task use_projection_as_singleton_and_inline()
    {
        #region sample_registering_projection_built_by_services

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMarten(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = "ioc";
                    })
                    // Note that this is chained after the call to AddMarten()
                    .AddProjectionWithServices<ProductProjection>(ProjectionLifecycle.Inline, ServiceLifetime.Singleton);

            })
            .StartAsync();

        #endregion

        var store = host.Services.GetRequiredService<IDocumentStore>();

        using var session = store.LightweightSession();
        var streamId = session.Events.StartStream<Product>(new ProductRegistered("Ankle Socks", "Socks")).Id;
        await session.SaveChangesAsync();

        var product = await session.LoadAsync<Product>(streamId);
        product.Price.ShouldBeGreaterThan(0);
        product.Name.ShouldBe("Ankle Socks");

    }

    [Fact]
    public async Task use_projection_as_singleton_and_async()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMarten(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = "ioc";
                    })
                    .AddAsyncDaemon(DaemonMode.Solo)
                    .AddProjectionWithServices<ProductProjection>(ProjectionLifecycle.Async, ServiceLifetime.Singleton);

            }).StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        using var session = store.LightweightSession();
        var streamId = session.Events.StartStream<Product>(new ProductRegistered("Ankle Socks", "Socks")).Id;
        await session.SaveChangesAsync();

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllShards();

        await daemon.Tracker.WaitForShardState("Product:All", 1);

        var product = await session.LoadAsync<Product>(streamId);
        product.Price.ShouldBeGreaterThan(0);
        product.Name.ShouldBe("Ankle Socks");

    }

    [Fact]
    public async Task use_projection_as_scoped_and_inline()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";
                }).AddProjectionWithServices<ProductProjection>(ProjectionLifecycle.Inline, ServiceLifetime.Scoped);

            }).StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();

        using var session = store.LightweightSession();
        var streamId = session.Events.StartStream<Product>(new ProductRegistered("Ankle Socks", "Socks")).Id;
        await session.SaveChangesAsync();

        var product = await session.LoadAsync<Product>(streamId);
        product.Price.ShouldBeGreaterThan(0);
        product.Name.ShouldBe("Ankle Socks");

    }
}


public interface IPriceLookup
{
    double PriceFor(string category);
}

public class PriceLookup: IPriceLookup
{
    public double PriceFor(string category)
    {
        var price = Math.Abs(category.GetDeterministicHashCode()) * 0.1;
        return price;
    }
}

public class Product
{
    public Guid Id { get; set; }
    public double Price { get; set; }
    public string Category { get; set; }
    public string Name { get; set; }
}

public record ProductRegistered(string Name, string Category);

#region sample_ProductProjection

public class ProductProjection : CustomProjection<Product, Guid>
{
    private readonly IPriceLookup _lookup;

    // The lookup service would be injected by IoC
    public ProductProjection(IPriceLookup lookup)
    {
        _lookup = lookup;
        AggregateByStream();
        ProjectionName = "Product";
    }

    public override ValueTask ApplyChangesAsync(DocumentSessionBase session, EventSlice<Product, Guid> slice, CancellationToken cancellation,
        ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline)
    {
        slice.Aggregate ??= new Product{Id = slice.Id};

        foreach (var data in slice.AllData())
        {
            if (data is ProductRegistered r)
            {
                slice.Aggregate.Price = _lookup.PriceFor(r.Category);
                slice.Aggregate.Name = r.Name;
                slice.Aggregate.Category = r.Category;
            }
        }

        if (slice.Aggregate != null)
        {
            session.Store(slice.Aggregate);
        }

        return ValueTask.CompletedTask;
    }
}

#endregion
