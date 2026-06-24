#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables.Partitioning;
using Xunit;

namespace CoreTests.Partitioning;

public class MetricsSample
{
    public Guid Id { get; set; }

    // A duplicated, indexed timestamp used as the range partition key for time-based retention.
    public DateTimeOffset BucketEnd { get; set; }

    public double Value { get; set; }
}

/// <summary>
/// #4779 — range-partition a single-tenant document table by an arbitrary (non-tenant) DateTimeOffset
/// column, the classic time-series retention pattern. The public entry point already exists
/// (<see cref="Marten.MartenRegistry.DocumentMappingExpression{T}.PartitionOn"/>); these tests pin the
/// date-keyed scenario and, critically, that a Marten-managed monthly range does not drift on re-apply
/// (which depended on the Weasel partition-bound round-trip fix).
/// </summary>
public class Bug_4779_partition_document_by_date: IAsyncLifetime
{
    private readonly string _schema = "bug4779_p" + Environment.ProcessId;

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(_schema); } catch { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DocumentStore BuildManagedStore() => (DocumentStore)DocumentStore.For(opts =>
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = _schema;

        #region sample_partitioning_document_by_date_range

        opts.Schema.For<MetricsSample>()
            .Duplicate(x => x.BucketEnd)
            .PartitionOn(x => x.BucketEnd, x =>
            {
                x.ByRange()
                    .AddRange("2026_01",
                        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero))
                    .AddRange("2026_02",
                        new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                        new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
            });

        #endregion
    });

    private DocumentStore BuildExternallyManagedStore() => (DocumentStore)DocumentStore.For(opts =>
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = _schema;

        #region sample_partitioning_document_by_date_externally_managed

        opts.Schema.For<MetricsSample>()
            .Duplicate(x => x.BucketEnd)
            .PartitionOn(x => x.BucketEnd, x => x.ByExternallyManagedRangePartitions());

        #endregion
    });

    [Fact]
    public async Task marten_managed_monthly_range_partitioning_is_idempotent()
    {
        // 1. First deploy: create the partitioned table and store data into the monthly partitions.
        await using (var store = BuildManagedStore())
        {
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

            var table = new DocumentTable(store.Options.Storage.MappingFor(typeof(MetricsSample)));
            var partitioning = table.Partitioning.ShouldBeOfType<RangePartitioning>();
            partitioning.Columns.Single().ShouldBe("bucket_end");

            await using var session = store.LightweightSession();
            session.Store(new MetricsSample
            {
                Id = Guid.NewGuid(), BucketEnd = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero), Value = 1
            });
            session.Store(new MetricsSample
            {
                Id = Guid.NewGuid(), BucketEnd = new DateTimeOffset(2026, 2, 10, 6, 0, 0, TimeSpan.Zero), Value = 2
            });
            await session.SaveChangesAsync();
        }

        // 2. Second deploy (nothing changed): the diff must be None. Before the Weasel round-trip fix
        // the declared DateTimeOffset bounds never matched PostgreSQL's echoed-back literals, so every
        // startup reported a destructive partition rebuild.
        await using (var store = BuildManagedStore())
        {
            var migration = await store.Storage.CreateMigrationAsync();
            migration.Difference.ShouldBe(SchemaPatchDifference.None,
                "Re-applying an unchanged date-range-partitioned document table must not diff as a change");

            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        // 3. Data survived and is queryable.
        await using (var store = BuildManagedStore())
        {
            await using var session = store.QuerySession();
            (await session.Query<MetricsSample>().CountAsync()).ShouldBe(2);
        }
    }

    [Fact]
    public async Task externally_managed_range_partitioning_by_date()
    {
        await using var store = BuildExternallyManagedStore();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var table = new DocumentTable(store.Options.Storage.MappingFor(typeof(MetricsSample)));
        var partitioning = table.Partitioning.ShouldBeOfType<RangePartitioning>();
        partitioning.Columns.Single().ShouldBe("bucket_end");

        // pg_partman / a scheduler owns the child partitions, so Marten leaves them alone.
        table.IgnorePartitionsInMigration.ShouldBeTrue();

        // Re-apply is a no-op: Marten never touches the externally-managed partitions.
        var migration = await store.Storage.CreateMigrationAsync();
        migration.Difference.ShouldBe(SchemaPatchDifference.None);
    }
}
