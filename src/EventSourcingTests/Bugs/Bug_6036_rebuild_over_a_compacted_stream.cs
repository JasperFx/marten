using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

// #6036: AggregationRunner fast-forwards a slice over its Compacted<T> and keeps only the events after it. When the
// stream ends with the Compacted<T>, there are none, so the projection storage is handed a null last event, which
// IProjectionStorage declares nullable and Marten's StoreProjection dereferenced to set the revision.
public class Bug_6036_rebuild_over_a_compacted_stream: OneOffConfigurationsContext
{
    public sealed record PlainOpened(Guid Id);

    public sealed record PlainTouched(int Count);

    public sealed class Plain
    {
        public Guid Id { get; set; }
        public int Touches { get; set; }

        public static Plain Create(PlainOpened e) => new() { Id = e.Id };

        public void Apply(PlainTouched e) => Touches += e.Count;
    }

    private async Task<Guid> aCompactedStream()
    {
        var id = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Plain>(id, new PlainOpened(id), new PlainTouched(1), new PlainTouched(2));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            await session.Events.CompactStreamAsync<Plain>(id);
            await session.SaveChangesAsync();
        }

        return id;
    }

    [Fact]
    public async Task rebuild_an_inline_snapshot_over_a_stream_that_ends_compacted()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Plain>(SnapshotLifecycle.Inline));
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var id = await aCompactedStream();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Plain>(CancellationToken.None);

        await using var query = theStore.QuerySession();
        var plain = await query.LoadAsync<Plain>(id);
        plain.ShouldNotBeNull();
        plain.Touches.ShouldBe(3);

        // The revision is the stream's version, as it is for any other rebuilt snapshot
        (await query.MetadataForAsync(plain)).CurrentRevision.ShouldBe(3);
    }

    [Fact]
    public async Task an_async_snapshot_catches_up_over_a_stream_that_ends_compacted()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Plain>(SnapshotLifecycle.Async));
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var id = await aCompactedStream();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(TimeSpan.FromSeconds(30));

        await using var query = theStore.QuerySession();
        var plain = await query.LoadAsync<Plain>(id);
        plain.ShouldNotBeNull();
        plain.Touches.ShouldBe(3);
        (await query.MetadataForAsync(plain)).CurrentRevision.ShouldBe(3);
    }

    [Fact]
    public async Task rebuild_an_async_snapshot_over_a_stream_that_ends_compacted()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Plain>(SnapshotLifecycle.Async));
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var id = await aCompactedStream();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Plain>(CancellationToken.None);

        await using var query = theStore.QuerySession();
        var plain = await query.LoadAsync<Plain>(id);
        plain.ShouldNotBeNull();
        plain.Touches.ShouldBe(3);
        (await query.MetadataForAsync(plain)).CurrentRevision.ShouldBe(3);
    }

    public sealed class Versioned: IRevisioned
    {
        public Guid Id { get; set; }
        public int Touches { get; set; }
        public int Version { get; set; }

        public static Versioned Create(PlainOpened e) => new() { Id = e.Id };

        public void Apply(PlainTouched e) => Touches += e.Count;
    }

    [Fact]
    public async Task an_async_snapshot_already_past_the_start_catches_up_over_a_compaction()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Versioned>(SnapshotLifecycle.Async));
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Versioned>(id, new PlainOpened(id), new PlainTouched(1), new PlainTouched(2));
            await session.SaveChangesAsync();
        }

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(TimeSpan.FromSeconds(30));
            await daemon.StopAllAsync();
        }

        // The document is at version 3. Two more events, then compacting folds all five into the last one
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(id, new PlainTouched(3), new PlainTouched(4));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            await session.Events.CompactStreamAsync<Versioned>(id);
            await session.SaveChangesAsync();
        }

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(TimeSpan.FromSeconds(30));
        }

        await using var query = theStore.QuerySession();
        var versioned = await query.LoadAsync<Versioned>(id);
        versioned.ShouldNotBeNull();
        versioned.Touches.ShouldBe(10);
        versioned.Version.ShouldBe(5);
        (await query.MetadataForAsync(versioned)).CurrentRevision.ShouldBe(5);
    }

    [Fact]
    public async Task an_async_snapshot_without_a_version_member_catches_up_over_a_compaction()
    {
        StoreOptions(opts => opts.Projections.Snapshot<Plain>(SnapshotLifecycle.Async));
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<Plain>(id, new PlainOpened(id), new PlainTouched(1), new PlainTouched(2));
            await session.SaveChangesAsync();
        }

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(TimeSpan.FromSeconds(30));
            await daemon.StopAllAsync();
        }

        // The document is at version 3. Two more events, then compacting folds all five into the last one
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(id, new PlainTouched(3), new PlainTouched(4));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            await session.Events.CompactStreamAsync<Plain>(id);
            await session.SaveChangesAsync();
        }

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(TimeSpan.FromSeconds(30));
        }

        await using var query = theStore.QuerySession();
        var plain = await query.LoadAsync<Plain>(id);
        plain.ShouldNotBeNull();
        plain.Touches.ShouldBe(10);
        (await query.MetadataForAsync(plain)).CurrentRevision.ShouldBe(5);
    }
}
