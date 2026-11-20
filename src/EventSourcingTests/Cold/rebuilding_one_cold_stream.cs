#pragma warning disable MARTEN001 // cold event storage is experimental
using System;
using System.Threading.Tasks;
using EventSourcingTests.Cold.Matrix;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Cold;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Cold;

/// <summary>
///     The per-identity rebuild (#6071) of an async aggregate whose stream has a cold part. Offline, it folds the
///     stream with <c>AggregateStreamAsync</c>, which reads the cold part. Through the daemon, it would re-fold the
///     events in <c>mt_events</c> only, so it is refused, as late-event recovery's rebuilds are.
/// </summary>
public class rebuilding_one_cold_stream: OneOffConfigurationsContext
{
    private static readonly TimeSpan Generous = 60.Seconds();
    private readonly InMemoryColdEventStore _cold = new();

    private void configure(bool ignoresColdStreams = false)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            opts.Projections.Snapshot<AsyncKeyedCart>(SnapshotLifecycle.Async, asyncConfiguration: x =>
            {
                if (ignoresColdStreams) x.IgnoreColdStreams();
            });
        });
    }

    private async Task<string> coldStreamAsync(IProjectionDaemon? daemon, long coldThrough)
    {
        var key = Guid.NewGuid().ToString();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<AsyncKeyedCart>(key, new MatrixOpened("anne", new MatrixOrderNumber(key)),
                new MatrixItemAdded("a"), new MatrixItemAdded("b"), new MatrixItemAdded("c"));
            await session.SaveChangesAsync();
        }

        if (daemon == null)
        {
            using var catchUp = await theStore.BuildProjectionDaemonAsync();
            await catchUp.StartAllAsync();
            await theStore.Storage.Database.WaitForNonStaleProjectionDataAsync(Generous);
            await catchUp.StopAllAsync();
        }
        else
        {
            await theStore.Storage.Database.WaitForNonStaleProjectionDataAsync(Generous);
        }

        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, coldThrough);
        return key;
    }

    private async Task corruptAsync(string key)
    {
        await using var session = theStore.LightweightSession();
        session.Store(new AsyncKeyedCart { Id = key, Customer = "wrong", Items = ["x"] });
        await session.SaveChangesAsync();
    }

    private async Task<AsyncKeyedCart?> loadAsync(string key)
    {
        await using var query = theStore.QuerySession();
        return await query.LoadAsync<AsyncKeyedCart>(key);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task offline_the_stream_is_folded_with_its_cold_part(long coldThrough)
    {
        configure();
        var key = await coldStreamAsync(null, coldThrough);
        await corruptAsync(key);

        await theStore.Advanced.RebuildSingleStreamAsync<AsyncKeyedCart>(key);

        ColdMatrixRun.ShouldBeTheWholeStream(await loadAsync(key));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task through_the_daemon_running_the_shard_it_is_refused(long coldThrough)
    {
        configure();
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var key = await coldStreamAsync(daemon, coldThrough);
        await corruptAsync(key);

        var refused = await Should.ThrowAsync<IdentityRebuildRefusedException>(
            () => theStore.Advanced.RebuildSingleStreamAsync<AsyncKeyedCart>(key));
        refused.Reason.ShouldBe(IdentityRebuildRefusal.NotSupportedByStore);
        refused.Message.ShouldContain("cold");

        // Nothing was written
        (await loadAsync(key)).ShouldNotBeNull().Customer.ShouldBe("wrong");

        await daemon.StopAllAsync();
    }

    [Fact]
    public async Task the_daemons_own_rebuild_of_one_identity_is_refused_as_late_event_recovery_meets_it()
    {
        // Late-event recovery (#6007) rebuilds the identity of a late event through the daemon. On a cold stream the
        // re-fold would leave the cold part out, so it is refused; recovery logs that and leaves the stream alone.
        configure();
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var key = await coldStreamAsync(daemon, 2);

        var projectionName = theStore.Options.Projections.All[0].Name;
        var refused = await Should.ThrowAsync<IdentityRebuildRefusedException>(
            () => daemon.RebuildIdentityAsync(projectionName, StorageConstants.DefaultTenantId, key));
        refused.Reason.ShouldBe(IdentityRebuildRefusal.NotSupportedByStore);

        ColdMatrixRun.ShouldBeTheWholeStream(await loadAsync(key));

        await daemon.StopAllAsync();
    }

    [Fact]
    public async Task a_stream_without_a_cold_part_still_rebuilds_through_the_daemon()
    {
        configure();
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var key = Guid.NewGuid().ToString();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<AsyncKeyedCart>(key, new MatrixOpened("anne", new MatrixOrderNumber(key)),
                new MatrixItemAdded("a"), new MatrixItemAdded("b"), new MatrixItemAdded("c"));
            await session.SaveChangesAsync();
        }

        await theStore.Storage.Database.WaitForNonStaleProjectionDataAsync(Generous);
        await corruptAsync(key);

        await theStore.Advanced.RebuildSingleStreamAsync<AsyncKeyedCart>(key);

        ColdMatrixRun.ShouldBeTheWholeStream(await loadAsync(key));
        await daemon.StopAllAsync();
    }

    [Fact]
    public async Task a_projection_that_ignores_cold_streams_rebuilds_from_mt_events_through_the_daemon()
    {
        // Its own choice: the application derives the read models of cold streams itself
        configure(ignoresColdStreams: true);
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var key = await coldStreamAsync(daemon, 2);
        await corruptAsync(key);

        await theStore.Advanced.RebuildSingleStreamAsync<AsyncKeyedCart>(key);

        var rebuilt = (await loadAsync(key)).ShouldNotBeNull();
        rebuilt.Items.ShouldBe(["b", "c"]);

        await daemon.StopAllAsync();
    }
}
