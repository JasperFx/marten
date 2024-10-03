using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace StressTests;

[Collection("ioc")]
public class projections_with_IoC_services
{
    [Fact]
    public async Task can_apply_database_changes_at_runtime_with_projection_with_services()
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync("ioc");
            await conn.CloseAsync();
        }

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMarten(opts =>
                    {
                        opts.ApplyChangesLockId = opts.ApplyChangesLockId + 10; // just make it different
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = "ioc";
                        opts.ApplyChangesLockId = opts.ApplyChangesLockId + 1;
                    })
                    // Note that this is chained after the call to AddMarten()
                    .AddProjectionWithServices<ProductProjection>(
                        ProjectionLifecycle.Inline,
                        ServiceLifetime.Scoped
                    ).ApplyAllDatabaseChangesOnStartup();
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();
        var tables = store.Storage.AllObjects().OfType<DocumentTable>();

        tables.Any(x => x.DocumentType == typeof(Product)).ShouldBeTrue();

        var existing = await store.Storage.Database.ExistingTableFor(typeof(Product));
        existing.ShouldNotBeNull();
    }

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
                        opts.ApplyChangesLockId = opts.ApplyChangesLockId + 2;
                    })
                    // Note that this is chained after the call to AddMarten()
                    .AddProjectionWithServices<ProductProjection>(
                        ProjectionLifecycle.Inline,
                        ServiceLifetime.Singleton
                    );
            })
            .StartAsync();

        #endregion

        var store = host.Services.GetRequiredService<IDocumentStore>();

        await using var session = store.LightweightSession();
        var streamId = session.Events.StartStream<Product>(
            new ProductRegistered("Ankle Socks", "Socks")
        ).Id;
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
                        opts.ApplyChangesLockId = opts.ApplyChangesLockId + 3;
                    })
                    .AddProjectionWithServices<ProductProjection>(ProjectionLifecycle.Async, ServiceLifetime.Singleton);
            }).StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        await using var session = store.LightweightSession();
        var streamId = session.Events.StartStream<Product>(new ProductRegistered("Ankle Socks", "Socks")).Id;
        await session.SaveChangesAsync();

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        await daemon.Tracker.WaitForShardState("Product:All", 1);

        var product = await session.LoadAsync<Product>(streamId);
        product.ShouldNotBeNull();
        product.Price.ShouldBeGreaterThan(0);
        product.Name.ShouldBe("Ankle Socks");
    }

    [Fact]
    public async Task use_projection_as_scoped_and_inline()
    {
        using var host = await Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";
                    opts.ApplyChangesLockId = opts.ApplyChangesLockId + 4;
                }).AddProjectionWithServices<ProductProjection>(ProjectionLifecycle.Inline, ServiceLifetime.Scoped, "MyProjection");
            }).StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();

        store.Options.As<StoreOptions>().Projections.All.Single()
            .ShouldBeOfType<ScopedProjectionWrapper<ProductProjection>>().ProjectionName.ShouldBe("MyProjection");

        await using var session = store.LightweightSession();
        var streamId = session.Events.StartStream<Product>(new ProductRegistered("Ankle Socks", "Socks")).Id;
        await session.SaveChangesAsync();

        var product = await session.LoadAsync<Product>(streamId);
        product.Price.ShouldBeGreaterThan(0);
        product.Name.ShouldBe("Ankle Socks");
    }

    [Fact]
    public async Task get_async_shards_with_custom_name()
    {
        using var host = await Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";
                    opts.ApplyChangesLockId = opts.ApplyChangesLockId + 5;
                }).AddProjectionWithServices<ProductProjection>(ProjectionLifecycle.Async, ServiceLifetime.Scoped, "MyProjection");
            }).StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();

        var projectionSource = store.Options.As<StoreOptions>().Projections.All.Single();
        projectionSource
            .ShouldBeOfType<ScopedProjectionWrapper<ProductProjection>>().ProjectionName.ShouldBe("MyProjection");

        projectionSource.AsyncProjectionShards((DocumentStore)store).Single().Name.Identity.ShouldBe("MyProjection:All");


    }

    [Fact]
    public async Task can_apply_database_changes_at_runtime_with_projection_with_services_on_martenStore()
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync("ioc");
            await conn.CloseAsync();
        }

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                var mse = services.AddMartenStore<ICustomStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";
                    opts.ApplyChangesLockId = opts.ApplyChangesLockId + 6;
                });

                mse.AddProjectionWithServices<ProductProjection>(
                    ProjectionLifecycle.Inline,
                    ServiceLifetime.Scoped
                );

                mse.ApplyAllDatabaseChangesOnStartup();
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<ICustomStore>();
        var tables = store.Storage.AllObjects().OfType<DocumentTable>();

        tables.Any(x => x.DocumentType == typeof(Product)).ShouldBeTrue();

        var existing = await store.Storage.Database.ExistingTableFor(typeof(Product));
        existing.ShouldNotBeNull();
    }

    [Fact]
    public async Task use_projection_as_singleton_and_inline_on_martenStore()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMartenStore<ICustomStore>(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = "ioc";
                        opts.ApplyChangesLockId = opts.ApplyChangesLockId + 7;
                    })
                    // Note that this is chained after the call to AddMartenStore()
                    .AddProjectionWithServices<ProductProjection>(
                        ProjectionLifecycle.Inline,
                        ServiceLifetime.Singleton
                    ).ApplyAllDatabaseChangesOnStartup();
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<ICustomStore>();

        await using var session = store.LightweightSession();
        var streamId = session.Events.StartStream<Product>(
            new ProductRegistered("Ankle Socks", "Socks")
        ).Id;
        await session.SaveChangesAsync();

        var product = await session.LoadAsync<Product>(streamId);
        product.Price.ShouldBeGreaterThan(0);
        product.Name.ShouldBe("Ankle Socks");
    }

    [Fact]
    public async Task use_projection_as_singleton_and_async_on_martenStore()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMartenStore<ICustomStore>(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = "ioc";
                        opts.ApplyChangesLockId = opts.ApplyChangesLockId + 8;
                    })
                    .AddProjectionWithServices<ProductProjection>(ProjectionLifecycle.Async, ServiceLifetime.Singleton)
                    .ApplyAllDatabaseChangesOnStartup();
            }).StartAsync();

        var store = host.Services.GetRequiredService<ICustomStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        await using var session = store.LightweightSession();
        var streamId = session.Events.StartStream<Product>(new ProductRegistered("Ankle Socks", "Socks")).Id;
        await session.SaveChangesAsync();

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        await daemon.Tracker.WaitForShardState("Product:All", 1);

        var product = await session.LoadAsync<Product>(streamId);
        product.ShouldNotBeNull();
        product.Price.ShouldBeGreaterThan(0);
        product.Name.ShouldBe("Ankle Socks");
    }

    [Fact]
    public async Task use_projection_as_scoped_and_inline_on_martenStore()
    {
        using var host = await Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMartenStore<ICustomStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";
                    opts.ApplyChangesLockId = opts.ApplyChangesLockId + 9;
                }).AddProjectionWithServices<ProductProjection>(ProjectionLifecycle.Inline, ServiceLifetime.Scoped, "MyProjection")
                .ApplyAllDatabaseChangesOnStartup();
            }).StartAsync();

        var store = host.Services.GetRequiredService<ICustomStore>();

        store.Options.As<StoreOptions>().Projections.All.Single()
            .ShouldBeOfType<ScopedProjectionWrapper<ProductProjection>>().ProjectionName.ShouldBe("MyProjection");

        await using var session = store.LightweightSession();
        var streamId = session.Events.StartStream<Product>(new ProductRegistered("Ankle Socks", "Socks")).Id;
        await session.SaveChangesAsync();

        var product = await session.LoadAsync<Product>(streamId);
        product.Price.ShouldBeGreaterThan(0);
        product.Name.ShouldBe("Ankle Socks");
    }

    [Fact]
    public async Task get_async_shards_with_custom_name_on_martenStore()
    {
        using var host = await Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMartenStore<ICustomStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc";
                    opts.ApplyChangesLockId = opts.ApplyChangesLockId + 10;
                }).AddProjectionWithServices<ProductProjection>(ProjectionLifecycle.Async, ServiceLifetime.Scoped, "MyProjection")
                    .ApplyAllDatabaseChangesOnStartup();
            }).StartAsync();

        var store = host.Services.GetRequiredService<ICustomStore>();

        var projectionSource = store.Options.As<StoreOptions>().Projections.All.Single();
        projectionSource
            .ShouldBeOfType<ScopedProjectionWrapper<ProductProjection>>().ProjectionName.ShouldBe("MyProjection");

        projectionSource.AsyncProjectionShards((DocumentStore)store).Single().Name.Identity.ShouldBe("MyProjection:All");


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

public class ProductProjection: CustomProjection<Product, Guid>
{
    private readonly IPriceLookup _lookup;

    // The lookup service would be injected by IoC
    public ProductProjection(IPriceLookup lookup)
    {
        _lookup = lookup;
        AggregateByStream();
        ProjectionName = "Product";
    }

    public override ValueTask ApplyChangesAsync(
        DocumentSessionBase session,
        EventSlice<Product, Guid> slice,
        CancellationToken cancellation,
        ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline
    )
    {
        slice.Aggregate ??= new Product { Id = slice.Id };

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

public interface ICustomStore: IDocumentStore
{
}
