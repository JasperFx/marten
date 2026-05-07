using System;
using System.Collections.Generic;
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

// Stage-1 streams represent products.
public record ProductCreated(string Sku, string Name, decimal Price);

public class Product
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

public class ProductProjection: SingleStreamProjection<Product, Guid>
{
    public ProductProjection()
    {
        // INTENTIONALLY tiny — 1.35.0+ keeps this from leaking into downstream correctness;
        // see Bug_4329_fan_out_and_cache_limit.cache_limit_one_does_not_break_downstream_lookups.
        Options.CacheLimitPerTenant = 1;
    }

    public override Product Evolve(Product snapshot, Guid id, IEvent e)
    {
        switch (e.Data)
        {
            case ProductCreated created:
                snapshot = new Product
                {
                    Id = id,
                    Sku = created.Sku,
                    Name = created.Name,
                    Price = created.Price
                };
                break;
        }

        return snapshot;
    }
}

// Stage-2 stream represents an order. OrderPlacedWithLineItems carries the IDs of the
// products on the order — one event with many product references.
public record OrderPlacedWithLineItems(Guid[] ProductIds);

public class OrderSummary
{
    public Guid Id { get; set; }
    public List<OrderLineSummary> Lines { get; set; } = [];
    public decimal Total { get; set; }
}

public class OrderLineSummary
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

public class OrderSummaryProjection: MultiStreamProjection<OrderSummary, Guid>
{
    public OrderSummaryProjection()
    {
        Options.CacheLimitPerTenant = 1000;
        Identity<IEvent<OrderPlacedWithLineItems>>(e => e.StreamId);
    }

    #region sample_for_entity_ids_fan_out

    public override async Task EnrichEventsAsync(SliceGroup<OrderSummary, Guid> group,
        IQuerySession querySession, CancellationToken cancellation)
    {
        // OrderPlacedWithLineItems carries an array of ProductIds. ForEntityIds fans out
        // a single event to one References<Product> per resolved id, regardless of how
        // small the upstream's CacheLimitPerTenant is — JasperFx.Events 1.35.0 keeps
        // upstream caches at full size for the duration of the composite batch.
        await group
            .EnrichWith<Product>()
            .ForEvent<OrderPlacedWithLineItems>()
            .ForEntityIds(e => e.ProductIds)
            .AddReferences();
    }

    #endregion

    public override OrderSummary Evolve(OrderSummary snapshot, Guid id, IEvent e)
    {
        switch (e.Data)
        {
            case OrderPlacedWithLineItems:
                snapshot ??= new OrderSummary { Id = id };
                break;

            case References<Product> productRef:
                snapshot ??= new OrderSummary { Id = id };
                snapshot.Lines.Add(new OrderLineSummary
                {
                    ProductId = productRef.Entity.Id,
                    Sku = productRef.Entity.Sku,
                    Name = productRef.Entity.Name,
                    Price = productRef.Entity.Price
                });
                snapshot.Total += productRef.Entity.Price;
                break;
        }

        return snapshot;
    }
}

public class Bug_4329_fan_out_and_cache_limit: BugIntegrationContext
{
    [Fact]
    public async Task fan_out_one_event_to_many_upstream_references()
    {
        StoreOptions(opts =>
        {
            opts.Projections.CompositeProjectionFor("OrderComposite", projection =>
            {
                projection.Add<ProductProjection>();             // stage 1
                projection.Add<OrderSummaryProjection>(2);       // stage 2
            });
        });

        // Place 5 products via individual product streams.
        var skus = new[] { "A1", "B2", "C3", "D4", "E5" };
        var productIds = new Guid[skus.Length];
        for (var i = 0; i < skus.Length; i++)
        {
            productIds[i] = Guid.NewGuid();
            theSession.Events.StartStream<Product>(productIds[i],
                new ProductCreated(skus[i], $"Product {skus[i]}", 10m + i));
        }

        // One order references all 5 products in a single event.
        var orderId = Guid.NewGuid();
        theSession.Events.StartStream<OrderSummary>(orderId, new OrderPlacedWithLineItems(productIds));

        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(30.Seconds());

        var summary = await theSession.LoadAsync<OrderSummary>(orderId);
        summary.ShouldNotBeNull();
        summary.Lines.Count.ShouldBe(5);
        summary.Lines.Select(x => x.Sku).OrderBy(x => x).ShouldBe(skus.OrderBy(x => x));
        summary.Total.ShouldBe(skus.Select((_, i) => 10m + i).Sum());
    }

    [Fact]
    public async Task cache_limit_one_does_not_break_downstream_lookups()
    {
        // Regression for the second symptom from #4329: with CacheLimitPerTenant=1 on the
        // upstream ProductProjection, stage 2's ForEntityIds lookups previously hit the
        // database (which can't see in-flight upstream writes) for everything except the
        // single cache survivor. JasperFx.Events 1.35.0 defers compaction until the entire
        // composite batch finishes, so the upstream cache stays full while downstream stages
        // read from it.
        StoreOptions(opts =>
        {
            opts.Projections.CompositeProjectionFor("OrderComposite", projection =>
            {
                projection.Add<ProductProjection>();             // stage 1, cache = 1
                projection.Add<OrderSummaryProjection>(2);       // stage 2
            });
        });

        // Many products, all in the SAME composite batch. With cache=1, only one would
        // survive end-of-stage-1 compaction under the old behavior — the rest would miss
        // cache, fall through to LoadManyAsync, and find nothing in the DB (uncommitted).
        const int productCount = 20;
        var productIds = new Guid[productCount];
        for (var i = 0; i < productCount; i++)
        {
            productIds[i] = Guid.NewGuid();
            theSession.Events.StartStream<Product>(productIds[i],
                new ProductCreated($"P{i:000}", $"Product {i}", 1m * i));
        }

        var orderId = Guid.NewGuid();
        theSession.Events.StartStream<OrderSummary>(orderId, new OrderPlacedWithLineItems(productIds));

        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(30.Seconds());

        var summary = await theSession.LoadAsync<OrderSummary>(orderId);
        summary.ShouldNotBeNull();
        summary.Lines.Count.ShouldBe(productCount);
    }
}
