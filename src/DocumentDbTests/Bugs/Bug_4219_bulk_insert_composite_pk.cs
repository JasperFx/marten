using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_4219_bulk_insert_composite_pk : OneOffConfigurationsContext
{
    [Fact]
    public async Task bulk_insert_ignore_duplicates_with_range_partitioned_table()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<DailySnapshot>()
                .Identity(x => x.Id)
                .PartitionOn(x => x.Date, x =>
                {
                    x.ByRange()
                        .AddRange("q1_2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1))
                        .AddRange("q2_2026", new DateOnly(2026, 4, 1), new DateOnly(2026, 7, 1));
                });
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Insert initial data
        var initial = new[]
        {
            new DailySnapshot { Id = "sensor-1", Date = new DateOnly(2026, 1, 15), Value = 100 },
            new DailySnapshot { Id = "sensor-1", Date = new DateOnly(2026, 2, 15), Value = 200 },
        };

        await theStore.BulkInsertDocumentsAsync(initial);

        // Now bulk insert with IgnoreDuplicates:
        // - first two have same composite PKs as existing (should be skipped)
        // - third has same ID but different date (new composite PK — should be inserted)
        var second = new[]
        {
            new DailySnapshot { Id = "sensor-1", Date = new DateOnly(2026, 1, 15), Value = 999 },
            new DailySnapshot { Id = "sensor-1", Date = new DateOnly(2026, 2, 15), Value = 888 },
            new DailySnapshot { Id = "sensor-1", Date = new DateOnly(2026, 3, 15), Value = 300 },
        };

        await theStore.BulkInsertDocumentsAsync(second, BulkInsertMode.IgnoreDuplicates);

        await using var session = theStore.QuerySession();
        var results = await session.Query<DailySnapshot>()
            .Where(x => x.Id == "sensor-1")
            .OrderBy(x => x.Date)
            .ToListAsync();

        // Should have 3 rows: original Jan + Feb values preserved, new March added
        results.Count.ShouldBe(3);
        results[0].Value.ShouldBe(100); // original preserved
        results[1].Value.ShouldBe(200); // original preserved
        results[2].Value.ShouldBe(300); // new row
    }

    [Fact]
    public async Task bulk_insert_overwrite_with_range_partitioned_table()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<DailySnapshot>()
                .Identity(x => x.Id)
                .PartitionOn(x => x.Date, x =>
                {
                    x.ByRange()
                        .AddRange("q1_2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 1))
                        .AddRange("q2_2026", new DateOnly(2026, 4, 1), new DateOnly(2026, 7, 1));
                });
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Insert initial data
        var initial = new[]
        {
            new DailySnapshot { Id = "sensor-1", Date = new DateOnly(2026, 1, 15), Value = 100 },
            new DailySnapshot { Id = "sensor-1", Date = new DateOnly(2026, 2, 15), Value = 200 },
        };

        await theStore.BulkInsertDocumentsAsync(initial);

        // Overwrite with updated values — same composite PKs, different values
        var updated = new[]
        {
            new DailySnapshot { Id = "sensor-1", Date = new DateOnly(2026, 1, 15), Value = 999 },
            new DailySnapshot { Id = "sensor-1", Date = new DateOnly(2026, 2, 15), Value = 888 },
        };

        await theStore.BulkInsertDocumentsAsync(updated, BulkInsertMode.OverwriteExisting);

        await using var session = theStore.QuerySession();
        var results = await session.Query<DailySnapshot>()
            .Where(x => x.Id == "sensor-1")
            .OrderBy(x => x.Date)
            .ToListAsync();

        results.Count.ShouldBe(2);
        results[0].Value.ShouldBe(999);
        results[1].Value.ShouldBe(888);
    }
}

public class DailySnapshot
{
    public string Id { get; set; } = default!;
    public DateOnly Date { get; set; }
    public decimal Value { get; set; }
}
