using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace DaemonTests.Composites;

public class Feature_4284_composite_projection_with_services
{
    [Fact]
    public async Task can_use_scoped_services_in_projection_registered_under_composite()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped<ICompositePriceLookup, CompositePriceLookup>();

                services.AddMarten(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = "feature_4284_net" + Environment.Version.Major;
                        opts.Projections.CompositeProjectionFor("Feature4284Products", projection =>
                        {
                            projection.AddProjectionWithServices<CompositeProductProjection>(ServiceLifetime.Scoped);
                            projection.AddProjectionWithServices<CompositeProductMetricProjection>(ServiceLifetime.Scoped);
                        });
                    })
                    .ApplyAllDatabaseChangesOnStartup();
            })
            .StartAsync();

        var store = host.Services.GetRequiredService<IDocumentStore>();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        await using var session = store.LightweightSession();
        var streamId = session.Events.StartStream<CompositeProduct>(
            new CompositeProductRegistered("Ankle Socks", "Socks")).Id;
        await session.SaveChangesAsync();

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(10.Seconds());
        await daemon.StopAllAsync();

        var product = await session.LoadAsync<CompositeProduct>(streamId);
        product.ShouldNotBeNull();
        product.Name.ShouldBe("Ankle Socks");
        product.Category.ShouldBe("Socks");
        product.Price.ShouldBe(12.5);

        var metric = await session.LoadAsync<CompositeProductMetric>(streamId);
        metric.ShouldNotBeNull();
        metric.Price.ShouldBe(12.5);
    }
}

public interface ICompositePriceLookup
{
    double PriceFor(string category);
}

public class CompositePriceLookup: ICompositePriceLookup
{
    public double PriceFor(string category)
    {
        return category == "Socks" ? 12.5 : 5;
    }
}

public class CompositeProduct
{
    public Guid Id { get; set; }
    public double Price { get; set; }
    public string Category { get; set; }
    public string Name { get; set; }
}

public record CompositeProductRegistered(string Name, string Category);

public class CompositeProductMetric
{
    public Guid Id { get; set; }
    public double Price { get; set; }
}

public class CompositeProductProjection: SingleStreamProjection<CompositeProduct, Guid>
{
    private readonly ICompositePriceLookup _lookup;

    public CompositeProductProjection(ICompositePriceLookup lookup)
    {
        _lookup = lookup;
        Name = "Feature4284Product";
    }

    public override CompositeProduct Evolve(CompositeProduct snapshot, Guid id, IEvent e)
    {
        snapshot ??= new CompositeProduct { Id = id };

        if (e.Data is CompositeProductRegistered registered)
        {
            snapshot.Price = _lookup.PriceFor(registered.Category);
            snapshot.Name = registered.Name;
            snapshot.Category = registered.Category;
        }

        return snapshot;
    }
}

public class CompositeProductMetricProjection: IProjection
{
    private readonly ICompositePriceLookup _lookup;

    public CompositeProductMetricProjection(ICompositePriceLookup lookup)
    {
        _lookup = lookup;
    }

    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events,
        CancellationToken cancellation)
    {
        foreach (var e in events)
        {
            if (e.Data is CompositeProductRegistered registered)
            {
                operations.Store(new CompositeProductMetric
                {
                    Id = e.StreamId,
                    Price = _lookup.PriceFor(registered.Category)
                });
            }
        }

        return Task.CompletedTask;
    }
}
