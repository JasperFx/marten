using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using EventSourcingTests.FetchForWriting;
using JasperFx.Events;
using Marten;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql.Tables;
using Xunit;

namespace EventSourcingTests;

public class advanced_async_tracking : OneOffConfigurationsContext, IAsyncLifetime
{
    private HighWaterDetector theDetector;

    public advanced_async_tracking()
    {
        StoreOptions(opts =>
        {
            opts.Events.EnableAdvancedAsyncTracking = true;
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);
        });
    }

    [Fact]
    public async Task should_have_the_skips_table()
    {
        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        var existingTables = await theStore.Storage.Database.SchemaTables();
        var skipTable = existingTables.Single(x => x.Name == "mt_high_water_skips");
        skipTable.ShouldNotBeNull();
    }

    [Fact]
    public async Task should_have_the_mt_mark_progression_with_skip_function()
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var functions = await theStore.Storage.Database.Functions();
        functions.ShouldContain(new DbObjectName(SchemaName, "mt_mark_progression_with_skip"));
    }

    [Fact]
    public async Task can_apply_and_assert_correctly()
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    public async Task InitializeAsync()
    {
        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
        theDetector = new HighWaterDetector((MartenDatabase)theStore.Storage.Database, theStore.Options.EventGraph, NullLogger.Instance);
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task try_mark_from_nothing_bootstraps_the_row_and_records_the_skip()
    {
        // #4425: the null-row branch used to return 0 unconditionally, which left
        // HighWaterAgent.CheckNowAsync polling forever waiting for the mark to advance.
        // The function now bootstraps the high-water row at the ending sequence and
        // records the implicit 0->ending skip so callers can observe forward progress.
        var final = await theDetector.TryMarkHighWaterSkippingAsync(1000, 100, CancellationToken.None);
        final.ShouldBe(1000);

        var skips = await theDetector.FetchLastProgressionSkipsAsync(100, CancellationToken.None);
        skips[0].Ending.ShouldBe(1000);
        skips[0].Starting.ShouldBe(100);
    }

    [Fact]
    public async Task happy_path_mark_the_skip()
    {
        await theDetector.MarkHighWaterMarkInDatabaseAsync(1000, CancellationToken.None);

        var latest = await theDetector.TryMarkHighWaterSkippingAsync(1100, 1000, CancellationToken.None);

        latest.ShouldBe(1100);

        var skips = await theDetector.FetchLastProgressionSkipsAsync(100, CancellationToken.None);

        skips[0].Ending.ShouldBe(1100);
        skips[0].Starting.ShouldBe(1000);

    }

    [Fact]
    public async Task sad_path_the_high_water_mark_has_moved()
    {
        await theDetector.MarkHighWaterMarkInDatabaseAsync(1100, CancellationToken.None);

        var latest = await theDetector.TryMarkHighWaterSkippingAsync(1050, 1000, CancellationToken.None);

        latest.ShouldBe(1100);

        var skips = await theDetector.FetchLastProgressionSkipsAsync(100, CancellationToken.None);
        skips.Any().ShouldBeFalse();
    }

    [Fact]
    public async Task rebuilding_async_projection_twice_does_not_violate_skips_pk_4530()
    {
        // #4530: a rebuild rewinds mt_event_progression and re-advances the high-water
        // mark, re-marking the same skip ending_sequence. Before the ON CONFLICT guard
        // the second pass threw 23505 on pkey_mt_high_water_skips_ending_sequence.
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "adv_async_rebuild_4530";
            opts.Events.EnableAdvancedAsyncTracking = true;
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        await store.Advanced.Clean.DeleteAllEventDataAsync();
        await store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(SimpleAggregate));

        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent(), new CEvent());
            await session.SaveChangesAsync();
        }

        using var daemon = await store.BuildProjectionDaemonAsync();

        // Two full rebuilds: the second re-marks the same high-water skip rows.
        await daemon.RebuildProjectionAsync<SimpleAggregate>(CancellationToken.None);
        await Should.NotThrowAsync(() => daemon.RebuildProjectionAsync<SimpleAggregate>(CancellationToken.None));

        await using var query = store.QuerySession();
        var aggregate = await query.LoadAsync<SimpleAggregate>(streamId);
        aggregate.ShouldNotBeNull();
    }
}
