using System;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// Follow-ups to #5532, which made a batched <c>FetchForExclusiveWriting</c> hold its row lock when the
/// transaction start has to open a physical connection. Each test here covers something that change left
/// open. Pooling is off throughout, so every session opens a fresh connection — the condition under which
/// <c>BeginTransactionAsync</c> does not complete synchronously, and therefore the only one where any of
/// this is observable.
/// </summary>
public class batched_exclusive_fetch_followups: OneOffConfigurationsContext
{
    private static string Unpooled =>
        new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString) { Pooling = false }.ToString();

    /// <summary>
    /// #5532 gave the <c>string</c> overload the identical change but covered only <c>Guid</c>. It is not
    /// the same code path underneath: <c>FindFetchPlan&lt;T, string&gt;</c> can resolve to
    /// <c>FetchNaturalKeyPlan</c>, which emits its own <c>for update of s</c>. The non-batched suite covers
    /// both stream identities side by side, so the batched one should too.
    /// </summary>
    [Fact]
    public async Task the_lock_is_held_for_a_string_identified_stream()
    {
        StoreOptions(opts =>
        {
            opts.Connection(Unpooled);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<SimpleAggregateAsString>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.StartStream<SimpleAggregateAsString>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        // Warm the schema-existence checks, or a cold check can yield long enough for the transaction
        // start to finish on its own and hide exactly what this is testing.
        await using (var warm = theStore.LightweightSession())
        {
            var warmBatch = warm.CreateBatchQuery();
            var warmItem = warmBatch.Events.FetchForExclusiveWriting<SimpleAggregateAsString>(streamId);
            await warmBatch.Execute();
            await warmItem;
        }

        await using var holder = theStore.LightweightSession();
        var batch = holder.CreateBatchQuery();
        var item = batch.Events.FetchForExclusiveWriting<SimpleAggregateAsString>(streamId);
        await batch.Execute();
        var stream = await item;
        stream.CurrentVersion.ShouldBe(2);

        await using (var other = theStore.LightweightSession())
        {
            await Should.ThrowAsync<StreamLockedException>(async () =>
            {
                await other.Events.FetchForExclusiveWriting<SimpleAggregateAsString>(streamId);
            });
        }

        stream.AppendOne(new CEvent());
        await holder.SaveChangesAsync();

        await using var after = theStore.LightweightSession();
        var next = await after.Events.FetchForExclusiveWriting<SimpleAggregateAsString>(streamId);
        next.CurrentVersion.ShouldBe(3);
    }

    /// <summary>
    /// A non-exclusive Async fetch and an exclusive one share a batch in either order, and the exclusive
    /// fetch's row lock survives the batch, to be released by <c>SaveChangesAsync</c>.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task a_mixed_batch_works_and_keeps_the_lock(bool exclusiveFirst)
    {
        StoreOptions(opts =>
        {
            opts.Connection(Unpooled);
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent());
        theSession.Events.StartStream<SimpleAggregate>(otherId, new BEvent());
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        var batch = session.CreateBatchQuery();

        Task<IEventStream<SimpleAggregate>> exclusive;
        Task<IEventStream<SimpleAggregate>> other;
        if (exclusiveFirst)
        {
            exclusive = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);
            other = batch.Events.FetchForWriting<SimpleAggregate>(otherId);
        }
        else
        {
            other = batch.Events.FetchForWriting<SimpleAggregate>(otherId);
            exclusive = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);
        }

        await batch.Execute();

        (await other).Aggregate!.BCount.ShouldBe(1);
        var locked = await exclusive;
        locked.Aggregate!.ACount.ShouldBe(1);

        await using var second = theStore.LightweightSession();
        var blocked = second.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);
        await Task.Delay(750);
        blocked.IsCompleted.ShouldBeFalse();

        locked.AppendOne(new CEvent());
        await session.SaveChangesAsync();
        (await blocked).Aggregate!.CCount.ShouldBe(1);
    }

    /// <summary>
    /// #5535. A non-exclusive <c>FetchForWriting</c> for an Async-snapshot aggregate works wherever it is
    /// enlisted in its batch.
    /// </summary>
    [Fact]
    public async Task an_async_snapshot_fetch_for_writing_can_come_after_other_operations()
    {
        StoreOptions(opts =>
        {
            opts.Connection(Unpooled);
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent());
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        var batch = session.CreateBatchQuery();

        // Anything at all ahead of it; a plain document load will do.
        var load = batch.Load<SimpleAggregate>(streamId);
        var item = batch.Events.FetchForWriting<SimpleAggregate>(streamId);
        await batch.Execute();

        (await load).ShouldBeNull();
        (await item).CurrentVersion.ShouldBe(1);
    }

    /// <summary>
    /// First in its batch.
    /// </summary>
    [Fact]
    public async Task an_async_snapshot_fetch_for_writing_is_fine_first_in_its_batch()
    {
        StoreOptions(opts =>
        {
            opts.Connection(Unpooled);
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent(), new BEvent());
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        var batch = session.CreateBatchQuery();
        var item = batch.Events.FetchForWriting<SimpleAggregate>(streamId);
        await batch.Execute();

        (await item).CurrentVersion.ShouldBe(2);
    }

    /// <summary>
    /// A regression guard, not a bug fix — and it passes against the merged code too, which is the point
    /// worth recording. The SQL was never sent for a cancelled token even before the follow-up, because
    /// <c>ExecuteReaderAsync(command, token)</c> observes it. What was wrong was only the <em>timing</em>:
    /// the wait on the transaction start deliberately does not observe the token (abandoning it would let
    /// it reassign the session's connection afterwards), so a request cancelled during a slow connection
    /// open waited out the whole Npgsql open timeout before failing. The <c>ThrowIfCancellationRequested</c>
    /// after the wait makes it fail promptly instead.
    /// <para>
    /// Keeping the test anyway: it pins that a cancelled <c>Execute</c> leaves no lock behind, which is the
    /// property a caller actually depends on, and it would catch a future change that sent the batch anyway.
    /// </para>
    /// </summary>
    [Fact]
    public async Task a_cancelled_execute_does_not_send_the_batch()
    {
        StoreOptions(opts =>
        {
            opts.Connection(Unpooled);
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.StartStream<SimpleAggregate>(streamId, new AEvent());
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();
        var batch = session.CreateBatchQuery();
        _ = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => batch.Execute(cancelled.Token));

        // The stream was never read for update, so nothing holds its lock.
        await using var other = theStore.LightweightSession();
        var stream = await other.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);
        stream.CurrentVersion.ShouldBe(1);
    }
}
