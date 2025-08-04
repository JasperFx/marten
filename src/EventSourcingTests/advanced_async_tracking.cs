using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.FetchForWriting;
using JasperFx.Events;
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
    public async Task try_mark_from_nothing_would_be_0()
    {
        var final = await theDetector.TryMarkHighWaterSkippingAsync(1000, 100, CancellationToken.None);
        final.ShouldBe(0);
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
}
