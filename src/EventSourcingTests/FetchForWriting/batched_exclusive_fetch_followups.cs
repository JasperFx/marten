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
    /// A non-exclusive <c>FetchForWriting</c> for an Async-snapshot aggregate brackets its reads in
    /// <c>begin transaction … end</c>, and that <c>end</c> commits the session's transaction — taking an
    /// exclusive fetch's row lock with it. The combination has never worked: the warm path already failed
    /// with <c>InvalidOperationException: This NpgsqlTransaction has completed</c> from
    /// <c>SaveChangesAsync</c>, and before #5532 the cold path silently committed with no lock at all.
    /// Refusing it at the offending call replaces an Npgsql message several layers away with one naming
    /// what the caller did.
    ///
    /// Both orderings, and the assertion is on <c>Execute()</c> rather than on the enlisting call, because
    /// only one ordering can throw synchronously: <c>FetchForExclusiveWriting</c> is <c>async</c>, so an
    /// exception in its body arrives as a faulted task, and a caller following the documented shape
    /// (enlist, <c>Execute()</c>, then await the items) would reach <c>Execute()</c> before observing it.
    /// <c>Execute()</c> refusing is therefore the guarantee that matters.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task a_mixed_batch_is_refused_with_an_explanation(bool exclusiveFirst)
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

        InvalidOperationException ex;

        if (exclusiveFirst)
        {
            // Exclusive first: the non-exclusive fetch is added second, through the synchronous AddItem,
            // so this ordering is caught at the offending call.
            _ = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);
            ex = Should.Throw<InvalidOperationException>(
                () => batch.Events.FetchForWriting<SimpleAggregate>(otherId));
        }
        else
        {
            // Non-exclusive first: the exclusive fetch faults its own task rather than throwing, so the
            // refusal has to come from Execute() -- otherwise the batch would be sent.
            _ = batch.Events.FetchForWriting<SimpleAggregate>(otherId);
            _ = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);
            ex = await Should.ThrowAsync<InvalidOperationException>(() => batch.Execute());
        }

        ex.Message.ShouldContain("cannot share a batch");
        ex.Message.ShouldContain("ProjectionLifecycle.Async");
    }

    /// <summary>
    /// Whichever order they were enlisted in, <c>Execute()</c> must refuse rather than send a batch whose
    /// <c>end</c> would commit the session's transaction out from under the exclusive fetch's lock.
    /// </summary>
    [Fact]
    public async Task execute_refuses_a_mixed_batch_enlisted_exclusive_first()
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
        _ = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(streamId);

        // Swallow the enlist-time refusal so the batch is left in the mixed state Execute() must catch.
        try
        {
            _ = batch.Events.FetchForWriting<SimpleAggregate>(otherId);
        }
        catch (InvalidOperationException)
        {
        }

        await Should.ThrowAsync<InvalidOperationException>(() => batch.Execute());
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
