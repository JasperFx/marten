using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

// A batched FetchForExclusiveWriting must hold the stream's row lock until the session commits or is
// disposed, exactly like the non-batched call. The batched form is the one Wolverine's
// [Aggregate(LoadStyle = ConcurrencyStyle.Exclusive)] codegen emits:
//
//     var item = batch.Events.FetchForExclusiveWriting<T>(id);
//     await batch.Execute();
//     var stream = await item;
//
// The lock is only real if the `select ... for update` runs inside the session's transaction. When
// starting that transaction has to open a physical connection, BeginTransactionAsync does not complete
// synchronously, and Execute must not send the batch before it has. Pooling is turned off here to make
// every session open a fresh connection: the same condition a pooled application hits after idle
// connections are pruned, or when a burst outnumbers the idle pool.
public class batched_exclusive_fetch_holds_the_stream_lock: OneOffConfigurationsContext
{
    [Theory]
    [InlineData(ProjectionLifecycle.Inline)]
    [InlineData(ProjectionLifecycle.Async)]
    [InlineData(ProjectionLifecycle.Live)]
    public async Task the_lock_is_held_after_the_batch_executes_on_a_fresh_connection(ProjectionLifecycle lifecycle)
    {
        var unpooled = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString) { Pooling = false }.ToString();
        StoreOptions(opts =>
        {
            opts.Connection(unpooled);
            if (lifecycle == ProjectionLifecycle.Inline) opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);
            if (lifecycle == ProjectionLifecycle.Async) opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        // Warm Marten's schema-existence checks so Execute reaches the database without yielding first.
        // Otherwise a cold check can happen to give BeginTransactionAsync time to finish and hide the bug.
        await using (var warm = theStore.LightweightSession())
        {
            await fetchBatched(warm, streamId);
        }

        await using var holder = theStore.LightweightSession();
        var stream = await fetchBatched(holder, streamId);
        stream.CurrentVersion.ShouldBe(2);

        await using (var other = theStore.LightweightSession())
        {
            await Should.ThrowAsync<StreamLockedException>(async () =>
            {
                await other.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);
            });
        }

        // The lock-holding transaction is the one SaveChangesAsync commits: the append lands and the
        // stream is free again afterwards.
        stream.AppendOne(new CEvent());
        await holder.SaveChangesAsync();

        await using var after = theStore.LightweightSession();
        var next = await after.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);
        next.CurrentVersion.ShouldBe(3);
    }

    // Two exclusive fetches in one batch share one transaction start, and both streams stay locked.
    [Fact]
    public async Task both_locks_are_held_when_one_batch_fetches_two_streams_exclusively()
    {
        var unpooled = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString) { Pooling = false }.ToString();
        StoreOptions(opts =>
        {
            opts.Connection(unpooled);
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);
        });

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(first, new AEvent());
        theSession.Events.StartStream<SimpleAggregate>(second, new BEvent());
        await theSession.SaveChangesAsync();

        await using (var warm = theStore.LightweightSession())
        {
            await fetchBatched(warm, first);
        }

        await using var holder = theStore.LightweightSession();
        var batch = holder.CreateBatchQuery();
        var one = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(first);
        var two = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(second);
        await batch.Execute();
        (await one).CurrentVersion.ShouldBe(1);
        (await two).CurrentVersion.ShouldBe(1);

        foreach (var id in new[] { first, second })
        {
            await using var other = theStore.LightweightSession();
            await Should.ThrowAsync<StreamLockedException>(async () =>
            {
                await other.Events.FetchForExclusiveWriting<SimpleAggregate>(id);
            });
        }
    }

    private static async Task<IEventStream<SimpleAggregate>> fetchBatched(Marten.IDocumentSession session, Guid streamId)
    {
        var batch = session.CreateBatchQuery();
        var item = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);
        await batch.Execute();
        return await item;
    }
}
