using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace ContainerScopedProjectionTests;

/// <summary>
///     Regression coverage for GitHub issue #4267:
///     <c>AddProjectionWithServices</c> was only available on the
///     <c>MartenConfigurationExpression</c> / <c>MartenStoreExpression{T}</c>
///     builder chain returned by <c>AddMarten()</c>. In modular-monolith setups
///     individual modules only have <see cref="IServiceCollection"/> — the builder
///     has already been consumed at composition root — so the builder-only API is
///     unusable and forces callers into the undiscoverable
///     <c>TProjection.Register{T}(services, …)</c> JasperFx internal.
///
///     This suite asserts that the new <see cref="IServiceCollection"/> overloads
///     behave identically to the builder form for both the default store and
///     ancillary stores.
/// </summary>
[Collection("ioc")]
public class projections_registered_directly_on_IServiceCollection
{
    [Fact]
    public async Task add_projection_with_services_on_IServiceCollection_registers_for_default_store()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                // Builder consumed once …
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc_4267_default";
                    opts.ApplyChangesLockId = opts.ApplyChangesLockId + 4267;
                });

                // … then a separate module/registration adds the projection using
                // only IServiceCollection — no builder chain required.
                services.AddProjectionWithServices<ProductProjection>(
                    ProjectionLifecycle.Inline,
                    ServiceLifetime.Singleton);
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();

        await using var session = store.LightweightSession();
        var streamId = session.Events.StartStream<Product>(
            new ProductRegistered("Ankle Socks", "Socks")).Id;
        await session.SaveChangesAsync();

        var product = await session.LoadAsync<Product>(streamId);
        product.ShouldNotBeNull();
        product!.Price.ShouldBeGreaterThan(0);   // proves the IoC-resolved IPriceLookup ran
        product.Name.ShouldBe("Ankle Socks");
    }

    [Fact]
    public async Task add_projection_with_services_on_IServiceCollection_registers_for_ancillary_store()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IPriceLookup, PriceLookup>();

                services.AddMartenStore<IProductsStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "ioc_4267_ancillary";
                    opts.ApplyChangesLockId = opts.ApplyChangesLockId + 4268;
                });

                services.AddProjectionWithServices<ProductProjection, IProductsStore>(
                    ProjectionLifecycle.Inline,
                    ServiceLifetime.Singleton);
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<IProductsStore>();

        await using var session = store.LightweightSession();
        var streamId = session.Events.StartStream<Product>(
            new ProductRegistered("Dress Socks", "Socks")).Id;
        await session.SaveChangesAsync();

        var product = await session.LoadAsync<Product>(streamId);
        product.ShouldNotBeNull();
        product!.Name.ShouldBe("Dress Socks");

        // Projection materialized a table in the ancillary store's schema.
        var tables = store.Storage.AllObjects().OfType<DocumentTable>();
        tables.ShouldContain(x => x.DocumentType == typeof(Product));
    }
}

public interface IProductsStore : IDocumentStore;
