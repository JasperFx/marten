using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// An Async fetch folds the events after the snapshot onto the snapshot document. That document
/// must not be one the session can hand back: a second fetch of the same stream reads the same
/// events from the database, and folding them onto a document that already has them applies them
/// twice. Every fact here has a snapshot at two events with two more after it, so ACount is 4.
/// </summary>
public class fetching_an_async_aggregate_twice_in_one_session: OneOffConfigurationsContext
{
    private async Task<Guid> streamWithASnapshotBehindItAsync()
    {
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async));

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<SimpleAggregate>(id, new AEvent(), new AEvent());
            await session.SaveChangesAsync();
        }

        await catchUpAsync();
        await appendAsync(id, 2);

        return id;
    }

    private async Task catchUpAsync()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(15.Seconds());
        await daemon.StopAllAsync();
    }

    private async Task appendAsync(Guid id, int count)
    {
        await using var session = theStore.LightweightSession();
        for (var i = 0; i < count; i++)
        {
            session.Events.Append(id, new AEvent());
        }

        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task fetch_latest_twice()
    {
        var id = await streamWithASnapshotBehindItAsync();
        await using var session = theStore.LightweightSession();

        (await session.Events.FetchLatest<SimpleAggregate>(id))!.ACount.ShouldBe(4);
        (await session.Events.FetchLatest<SimpleAggregate>(id))!.ACount.ShouldBe(4);
    }

    [Fact]
    public async Task fetch_for_writing_twice()
    {
        var id = await streamWithASnapshotBehindItAsync();
        await using var session = theStore.LightweightSession();

        (await session.Events.FetchForWriting<SimpleAggregate>(id)).Aggregate!.ACount.ShouldBe(4);
        (await session.Events.FetchForWriting<SimpleAggregate>(id)).Aggregate!.ACount.ShouldBe(4);
    }

    [Fact]
    public async Task fetch_for_writing_with_an_expected_version_twice()
    {
        var id = await streamWithASnapshotBehindItAsync();
        await using var session = theStore.LightweightSession();

        (await session.Events.FetchForWriting<SimpleAggregate>(id, 4)).Aggregate!.ACount.ShouldBe(4);
        (await session.Events.FetchForWriting<SimpleAggregate>(id, 4)).Aggregate!.ACount.ShouldBe(4);
    }

    [Fact]
    public async Task fetch_for_exclusive_writing_twice()
    {
        var id = await streamWithASnapshotBehindItAsync();
        await using var session = theStore.LightweightSession();

        (await session.Events.FetchForExclusiveWriting<SimpleAggregate>(id)).Aggregate!.ACount.ShouldBe(4);
        (await session.Events.FetchForExclusiveWriting<SimpleAggregate>(id)).Aggregate!.ACount.ShouldBe(4);
    }

    [Fact]
    public async Task batched_fetch_after_a_direct_one()
    {
        var id = await streamWithASnapshotBehindItAsync();
        await using var session = theStore.LightweightSession();

        (await session.Events.FetchLatest<SimpleAggregate>(id))!.ACount.ShouldBe(4);

        var batch = session.CreateBatchQuery();
        var fetch = batch.Events.FetchLatest<SimpleAggregate>(id);
        await batch.Execute();

        (await fetch)!.ACount.ShouldBe(4);
    }

    [Fact]
    public async Task a_document_the_caller_loaded_first_is_not_changed_by_a_fetch()
    {
        var id = await streamWithASnapshotBehindItAsync();
        await using var session = theStore.IdentitySession();

        var loaded = await session.LoadAsync<SimpleAggregate>(id);
        loaded!.ACount.ShouldBe(2);

        (await session.Events.FetchLatest<SimpleAggregate>(id))!.ACount.ShouldBe(4);

        loaded.ACount.ShouldBe(2);
    }

    [Fact]
    public async Task an_append_between_the_fetches_is_applied_once()
    {
        var id = await streamWithASnapshotBehindItAsync();
        await using var session = theStore.LightweightSession();

        (await session.Events.FetchLatest<SimpleAggregate>(id))!.ACount.ShouldBe(4);

        await appendAsync(id, 1);

        (await session.Events.FetchLatest<SimpleAggregate>(id))!.ACount.ShouldBe(5);
    }

    [Fact]
    public async Task a_snapshot_that_moves_between_the_fetches_is_read_again()
    {
        var id = await streamWithASnapshotBehindItAsync();
        await catchUpAsync();

        await using var session = theStore.LightweightSession();

        (await session.Events.FetchLatest<SimpleAggregate>(id))!.ACount.ShouldBe(4);

        await appendAsync(id, 1);
        await catchUpAsync();

        (await session.Events.FetchLatest<SimpleAggregate>(id))!.ACount.ShouldBe(5);
    }
}
