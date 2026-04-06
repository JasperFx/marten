using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug4223Snapshot
{
    public string Id { get; set; } = "";
    public DateOnly Date { get; set; }
    public decimal Quantity { get; set; }
}

public class Bug_4223_update_partitioned_composite_pk : OneOffConfigurationsContext
{
    public Bug_4223_update_partitioned_composite_pk()
    {
        _schemaName = "bug4223";
    }

    [Fact]
    public async Task update_with_same_id_different_partition_key()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Bug4223Snapshot>()
                .Identity(x => x.Id)
                .Duplicate(x => x.Date)
                .PartitionOn(x => x.Date, x =>
                {
                    x.ByRange()
                        .AddRange("q1_2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1))
                        .AddRange("q2_2026", new DateOnly(2026, 4, 1), new DateOnly(2026, 7, 1));
                });
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Insert two documents with same id, different date (valid per composite PK)
        var d1 = new Bug4223Snapshot { Id = "sensor-1", Date = new DateOnly(2026, 1, 15), Quantity = 100 };
        var d2 = new Bug4223Snapshot { Id = "sensor-1", Date = new DateOnly(2026, 3, 15), Quantity = 200 };

        await using (var session = theStore.LightweightSession())
        {
            session.Store(d1);
            session.Store(d2);
            await session.SaveChangesAsync();
        }

        // Update only the first one — should NOT affect the second
        await using (var session = theStore.LightweightSession())
        {
            d1.Quantity = 999;
            session.Update(d1);
            await session.SaveChangesAsync();
        }

        // Verify: d1 updated, d2 unchanged
        await using (var query = theStore.QuerySession())
        {
            var results = await query.Query<Bug4223Snapshot>()
                .Where(x => x.Id == "sensor-1")
                .OrderBy(x => x.Date)
                .ToListAsync();

            results.Count.ShouldBe(2);
            results[0].Quantity.ShouldBe(999m);  // updated
            results[1].Quantity.ShouldBe(200m);  // unchanged
        }
    }

    [Fact]
    public async Task upsert_with_same_id_different_partition_key()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Bug4223Snapshot>()
                .Identity(x => x.Id)
                .Duplicate(x => x.Date)
                .PartitionOn(x => x.Date, x =>
                {
                    x.ByRange()
                        .AddRange("q1_2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1))
                        .AddRange("q2_2026", new DateOnly(2026, 4, 1), new DateOnly(2026, 7, 1));
                });
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Store two documents with same id, different dates
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new Bug4223Snapshot { Id = "sensor-1", Date = new DateOnly(2026, 1, 15), Quantity = 100 });
            session.Store(new Bug4223Snapshot { Id = "sensor-1", Date = new DateOnly(2026, 3, 15), Quantity = 200 });
            await session.SaveChangesAsync();
        }

        // Upsert the first one with new quantity
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new Bug4223Snapshot { Id = "sensor-1", Date = new DateOnly(2026, 1, 15), Quantity = 777 });
            await session.SaveChangesAsync();
        }

        await using (var query = theStore.QuerySession())
        {
            var results = await query.Query<Bug4223Snapshot>()
                .Where(x => x.Id == "sensor-1")
                .OrderBy(x => x.Date)
                .ToListAsync();

            results.Count.ShouldBe(2);
            results[0].Quantity.ShouldBe(777m);  // upserted
            results[1].Quantity.ShouldBe(200m);  // unchanged
        }
    }
}
