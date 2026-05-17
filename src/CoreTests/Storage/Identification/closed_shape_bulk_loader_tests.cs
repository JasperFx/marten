using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike M16: validates closed-shape BulkLoader. Drives the
/// Npgsql COPY binary protocol from the descriptor's column list
/// instead of an emitted subclass. Tests cover plain inserts,
/// auto-assigned ids, soft-delete column defaults, and the IDocumentStore
/// public BulkInsertAsync API.
/// </summary>
public class closed_shape_bulk_loader_tests: BugIntegrationContext
{
    private DocumentStore ClosedShapeStore(Action<StoreOptions>? extra = null)
        => StoreOptions(opts =>
        {
            opts.UseClosedShapeDocumentStorage = true;
            extra?.Invoke(opts);
        });

    [Fact]
    public async Task bulk_insert_round_trips_guid_id_docs()
    {
        var store = ClosedShapeStore();
        var docs = Enumerable.Range(0, 50)
            .Select(i => new BulkGuidDoc { Id = Guid.NewGuid(), Name = $"row-{i}" })
            .ToArray();

        await store.BulkInsertAsync(docs);

        await using var query = store.QuerySession();
        var count = await query.Query<BulkGuidDoc>().CountAsync();
        count.ShouldBe(50);

        var first = await query.LoadAsync<BulkGuidDoc>(docs[0].Id);
        first.ShouldNotBeNull();
        first.Name.ShouldBe("row-0");
    }

    [Fact]
    public async Task bulk_insert_auto_assigns_hilo_int_ids()
    {
        var store = ClosedShapeStore();
        var docs = Enumerable.Range(0, 25)
            .Select(i => new BulkIntDoc { Name = $"int-{i}" })
            .ToArray();

        await store.BulkInsertAsync(docs);

        // Every doc should have a positive id assigned.
        docs.ShouldAllBe(d => d.Id > 0);
        docs.Select(d => d.Id).Distinct().Count().ShouldBe(25);

        await using var query = store.QuerySession();
        (await query.Query<BulkIntDoc>().CountAsync()).ShouldBe(25);
    }

    [Fact]
    public async Task bulk_insert_into_soft_delete_table_defaults_to_alive()
    {
        var store = ClosedShapeStore(opts =>
        {
            opts.Schema.For<BulkSoftDeleteDoc>().SoftDeleted();
        });

        var docs = new[]
        {
            new BulkSoftDeleteDoc { Id = Guid.NewGuid(), Name = "alive-1" },
            new BulkSoftDeleteDoc { Id = Guid.NewGuid(), Name = "alive-2" }
        };

        await store.BulkInsertAsync(docs);

        await using var query = store.QuerySession();
        // Soft-delete LINQ filter excludes nothing because every row is
        // alive.
        (await query.Query<BulkSoftDeleteDoc>().CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task bulk_insert_supports_hierarchical_root_polymorphic()
    {
        var store = ClosedShapeStore(opts =>
        {
            opts.Schema.For<BulkShop>().AddSubClass<BulkCoffeeShop>();
        });

        var docs = new BulkShop[]
        {
            new BulkShop { Id = Guid.NewGuid(), Name = "plain" },
            new BulkCoffeeShop { Id = Guid.NewGuid(), Name = "Blue Bottle", Roast = "dark" }
        };

        await store.BulkInsertAsync(docs);

        await using var query = store.QuerySession();
        var allShops = await query.Query<BulkShop>().ToListAsync();
        allShops.Count.ShouldBe(2);

        var coffees = await query.Query<BulkCoffeeShop>().ToListAsync();
        coffees.Count.ShouldBe(1);
        coffees[0].Roast.ShouldBe("dark");
    }
}

public class BulkGuidDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class BulkIntDoc
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class BulkSoftDeleteDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class BulkShop
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class BulkCoffeeShop: BulkShop
{
    public string Roast { get; set; } = string.Empty;
}
