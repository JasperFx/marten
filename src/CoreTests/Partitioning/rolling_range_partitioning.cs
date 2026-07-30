#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Core.Partitioning;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables.Partitioning;
using Xunit;

namespace CoreTests.Partitioning;

public class RollingMetricsSample
{
    public Guid Id { get; set; }

    // Duplicated so it exists as a real timestamptz column, which is what the RANGE partition keys on.
    public DateTimeOffset BucketEnd { get; set; }

    public double Value { get; set; }
}

public static class RollingRangePartitionSamples
{
    public static void configure_a_rolling_monthly_window(StoreOptions opts)
    {
        #region sample_partitioning_document_by_rolling_range

        opts.Schema.For<MetricsSample>()
            .Duplicate(x => x.BucketEnd)
            // Keep 12 months of history, provision 3 months ahead. Marten creates the partitions at the
            // leading edge and drops the aged ones at the trailing edge -- no application-authored DDL.
            .PartitionOn(x => x.BucketEnd,
                x => x.ByRollingRange(PartitionPeriod.Month, periodsAhead: 3, periodsBehind: 12));

        #endregion
    }

    public static async Task run_the_maintenance_pass_yourself(IDocumentStore store, CancellationToken token)
    {
        #region sample_applying_rolling_partitions

        // Roll every rolling-window table forward to its current window and drop the partitions that have
        // aged past their retention floor. Idempotent, and safe to run from several nodes at once.
        await store.Advanced.ApplyRollingPartitionsAsync(token);

        #endregion
    }
}

/// <summary>
/// #5093 — Marten owns the partition set of a time-series document table through Weasel's
/// <see cref="ManagedRangePartitions"/> rolling window (weasel#401), so nothing has to reach for
/// <c>ByExternallyManagedRangePartitions()</c> and hand-write <c>CREATE TABLE ... PARTITION OF</c> /
/// <c>DROP TABLE</c>. The window is a pure function of the policy and the clock, so these tests move a
/// <see cref="FakeTimeProvider"/> rather than the calendar.
/// </summary>
public class rolling_range_partitioning: IAsyncLifetime
{
    private readonly string _schema = "rolling5093_p" + Environment.ProcessId;

    private static readonly DateTimeOffset July2026 = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    public async ValueTask InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.DropSchemaAsync(_schema);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private DocumentStore buildStore(TimeProvider clock, int periodsAhead = 1, int periodsBehind = 2) =>
        (DocumentStore)DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;

            opts.Schema.For<RollingMetricsSample>()
                .Duplicate(x => x.BucketEnd)
                .PartitionOn(x => x.BucketEnd,
                    x => x.ByRollingRange(PartitionPeriod.Month, periodsAhead, periodsBehind, clock));
        });

    private async Task<string[]> partitionNames()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var names = new List<string>();
        await using var reader = await conn.CreateCommand(
                """
                select c.relname
                from pg_inherits i
                join pg_class c on c.oid = i.inhrelid
                join pg_class p on p.oid = i.inhparent
                join pg_namespace n on n.oid = p.relnamespace
                where n.nspname = :schema and p.relname = 'mt_doc_rollingmetricssample'
                """)
            .With("schema", _schema)
            .ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            names.Add(await reader.GetFieldValueAsync<string>(0));
        }

        return names.OrderBy(x => x).ToArray();
    }

    [Fact]
    public void the_table_keeps_marten_managed_partition_reconciliation()
    {
        using var store = buildStore(new FakeTimeProvider(July2026));

        var table = new DocumentTable(store.Options.Storage.MappingFor(typeof(RollingMetricsSample)));

        var partitioning = table.Partitioning.ShouldBeOfType<RangePartitioning>();
        partitioning.Columns.Single().ShouldBe("bucket_end");
        partitioning.PartitionManager.ShouldBeOfType<ManagedRangePartitions>()
            .Policy.Period.ShouldBe(PartitionPeriod.Month);

        // The entire point of #5093: unlike ByExternallyManagedRangePartitions(), Marten keeps reconciling
        // this table's partitions instead of stepping out of the way.
        table.IgnorePartitionsInMigration.ShouldBeFalse();
    }

    [Fact]
    public void the_declared_window_is_the_policy_window()
    {
        using var store = buildStore(new FakeTimeProvider(July2026));

        var partitioning = (RangePartitioning)new DocumentTable(
            store.Options.Storage.MappingFor(typeof(RollingMetricsSample))).Partitioning!;

        // periodsBehind: 2, current, periodsAhead: 1
        partitioning.PartitionManager!.Partitions().Select(x => x.Suffix)
            .ShouldBe(["m202605", "m202606", "m202607", "m202608"]);
    }

    [Fact]
    public async Task creates_the_whole_window_plus_a_default_overflow_partition()
    {
        await using var store = buildStore(new FakeTimeProvider(July2026));
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await partitionNames()).ShouldBe([
            "mt_doc_rollingmetricssample_default",
            "mt_doc_rollingmetricssample_m202605",
            "mt_doc_rollingmetricssample_m202606",
            "mt_doc_rollingmetricssample_m202607",
            "mt_doc_rollingmetricssample_m202608"
        ]);
    }

    [Fact]
    public async Task re_applying_an_unmoved_window_is_a_no_op()
    {
        await using (var store = buildStore(new FakeTimeProvider(July2026)))
        {
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        await using var second = buildStore(new FakeTimeProvider(July2026));
        (await second.Storage.CreateMigrationAsync()).Difference.ShouldBe(SchemaPatchDifference.None);
    }

    [Fact]
    public async Task rolling_the_window_forward_is_additive_and_never_a_rebuild()
    {
        await using (var store = buildStore(new FakeTimeProvider(July2026)))
        {
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

            await using var session = store.LightweightSession();
            session.Store(new RollingMetricsSample
            {
                Id = Guid.NewGuid(), BucketEnd = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero), Value = 1
            });
            session.Store(new RollingMetricsSample
            {
                Id = Guid.NewGuid(), BucketEnd = July2026, Value = 2
            });
            await session.SaveChangesAsync();
        }

        // Two months later the declared window has both gained a leading-edge period and lost two trailing
        // ones. Before #5093 that shape resolved to PartitionDelta.Rebuild — a CREATE _temp / copy of a
        // multi-gigabyte table — which is exactly why teams reached for the externally-managed escape hatch.
        var clock = new FakeTimeProvider(July2026.AddMonths(2));
        await using var later = buildStore(clock);

        var migration = await later.Storage.CreateMigrationAsync();
        migration.Difference.ShouldBe(SchemaPatchDifference.Update);

        await later.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var names = await partitionNames();

        // Additive: the new leading edge exists...
        names.ShouldContain("mt_doc_rollingmetricssample_m202610");
        // ...and the aged partitions are still on disk. Migration NEVER destroys data — retention is a
        // separate, explicit policy pass (see drops_partitions_that_have_aged_past_the_retention_floor).
        names.ShouldContain("mt_doc_rollingmetricssample_m202605");

        // And the data from before the roll survived it.
        await using var query = later.QuerySession();
        (await query.Query<RollingMetricsSample>().CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task drops_partitions_that_have_aged_past_the_retention_floor()
    {
        await using (var store = buildStore(new FakeTimeProvider(July2026)))
        {
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

            await using var session = store.LightweightSession();
            session.Store(new RollingMetricsSample
            {
                Id = Guid.NewGuid(), BucketEnd = new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero), Value = 1
            });
            session.Store(new RollingMetricsSample
            {
                Id = Guid.NewGuid(), BucketEnd = July2026, Value = 2
            });
            await session.SaveChangesAsync();
        }

        await using var later = buildStore(new FakeTimeProvider(July2026.AddMonths(2)));
        await later.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await later.Advanced.ApplyRollingPartitionsAsync(CancellationToken.None);

        var names = await partitionNames();

        // Retention floor at 2026-09 minus 2 periods is 2026-07, so May and June are gone in O(1)...
        names.ShouldNotContain("mt_doc_rollingmetricssample_m202605");
        names.ShouldNotContain("mt_doc_rollingmetricssample_m202606");
        // ...and everything inside the window, plus the DEFAULT overflow, stayed.
        names.ShouldContain("mt_doc_rollingmetricssample_m202607");
        names.ShouldContain("mt_doc_rollingmetricssample_m202610");
        names.ShouldContain("mt_doc_rollingmetricssample_default");

        // The May row went with its partition; the July row is untouched.
        await using var query = later.QuerySession();
        (await query.Query<RollingMetricsSample>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task the_host_startup_pass_rolls_the_window_forward_and_retires_the_aged_partitions()
    {
        await using (var store = buildStore(new FakeTimeProvider(July2026)))
        {
            await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        // Redeploy two months later. Nothing here authors any DDL: startup rolls the window forward and
        // drops what has aged out, on exactly the schedule Marten already applies schema changes on.
        var clock = new FakeTimeProvider(July2026.AddMonths(2));
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = _schema;

                    opts.Schema.For<RollingMetricsSample>()
                        .Duplicate(x => x.BucketEnd)
                        .PartitionOn(x => x.BucketEnd, x => x.ByRollingRange(PartitionPeriod.Month, 1, 2, clock));
                }).ApplyAllDatabaseChangesOnStartup();
            })
            .StartAsync();

        var names = await partitionNames();

        names.ShouldContain("mt_doc_rollingmetricssample_m202610");
        names.ShouldNotContain("mt_doc_rollingmetricssample_m202605");
        names.ShouldNotContain("mt_doc_rollingmetricssample_m202606");

        await host.StopAsync();
    }

    [Fact]
    public async Task the_apply_pass_is_idempotent_and_safe_to_run_concurrently()
    {
        var clock = new FakeTimeProvider(July2026);
        await using var store = buildStore(clock);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var before = await partitionNames();

        // Several nodes starting at once all run the same pass.
        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => store.Advanced.ApplyRollingPartitionsAsync(CancellationToken.None)));

        (await partitionNames()).ShouldBe(before);
    }

    [Fact]
    public async Task rows_outside_the_provisioned_window_land_in_the_default_partition()
    {
        await using var store = buildStore(new FakeTimeProvider(July2026));
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Far outside [2026-05, 2026-09). A managed rolling range always carries a DEFAULT partition
        // precisely so this is stored rather than rejected with a 23514 check violation.
        await using var session = store.LightweightSession();
        session.Store(new RollingMetricsSample
        {
            Id = Guid.NewGuid(), BucketEnd = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero), Value = 3
        });
        await session.SaveChangesAsync();

        await using var query = store.QuerySession();
        (await query.Query<RollingMetricsSample>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public void refuses_a_partition_key_that_is_not_a_point_in_time()
    {
        using var store = (DocumentStore)DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;

            opts.Schema.For<RollingMetricsSample>()
                .Duplicate(x => x.Value)
                .PartitionOn(x => x.Value, x => x.ByRollingRange(PartitionPeriod.Month, 1, 2));
        });

        // A rolling window is a function of the clock, so a non-temporal partition key is caught at
        // configuration with a message that names the member, not as an opaque partition-bound error
        // during the first migration.
        Should.Throw<InvalidOperationException>(() => store.Options.Storage.MappingFor(typeof(RollingMetricsSample)))
            .Message.ShouldContain("DateTime or DateTimeOffset");
    }
}
