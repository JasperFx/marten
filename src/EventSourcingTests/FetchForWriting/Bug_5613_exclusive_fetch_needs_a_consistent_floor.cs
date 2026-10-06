using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten;
using Marten.Exceptions;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// #5613, split out of #5604. <c>FetchForExclusiveWriting</c> on an aggregate projected with
/// <c>ProjectionLifecycle.Async</c> runs its three reads inside the session's own transaction, opened
/// at the <c>SessionOptions</c> default of READ COMMITTED. Under READ COMMITTED every <em>statement</em>
/// takes a fresh snapshot even inside one transaction, so the snapshot-document read and the events
/// read can disagree exactly as in #5604: a daemon snapshot write landing between them leaves the
/// delta query — which re-reads <c>a.mt_version</c> — excluding the very events the document read
/// never saw.
///
/// <para>
/// The <c>for update</c> does not help. It locks the <c>mt_streams</c> row, and the daemon never
/// touches <c>mt_streams</c> — it writes the snapshot table and <c>mt_event_progression</c>. So the one
/// lock this path takes excludes other writers of the stream, which is its job, and does nothing about
/// this race.
/// </para>
///
/// <para>
/// The #5604 bracket is not available here: this path already owns a transaction, PostgreSQL will not
/// change the isolation level of a transaction that has run a statement, and the bracket's <c>end</c>
/// would commit the transaction holding the row lock. Raising the session's isolation level is worse
/// than the bug — that transaction carries the whole <c>SaveChangesAsync</c>, and per #5528 Marten
/// deliberately does not retry writes at RepeatableRead or above.
/// </para>
/// </summary>
public class Bug_5613_exclusive_fetch_needs_a_consistent_floor: OneOffConfigurationsContext
{
    /// <summary>
    /// Holds the snapshot document read open long enough for the daemon to commit a snapshot
    /// underneath it. Keyed off the SHAPE of the batch — a document load by id followed by an
    /// mt_events read — so it only stretches the read under test and goes inert rather than silently
    /// delaying something else if the batch stops being built this way.
    /// </summary>
    public sealed class DelaySnapshotRead(double seconds): IMartenSessionLogger
    {
        public void OnBeforeExecute(NpgsqlBatch batch)
        {
            var commands = batch.BatchCommands.Cast<NpgsqlBatchCommand>().ToList();
            var snapshotRead = commands.FindIndex(c =>
                c.CommandText.Contains("mt_doc_simpleaggregate") && c.CommandText.Contains("where id = "));

            if (snapshotRead >= 0 &&
                commands.Skip(snapshotRead + 1).Any(c => c.CommandText.Contains("mt_events as d")))
            {
                commands[snapshotRead].CommandText +=
                    $" and (select true from pg_sleep({seconds.ToString(CultureInfo.InvariantCulture)}))";
            }
        }

        public void OnBeforeExecute(NpgsqlCommand command) { }
        public void LogSuccess(NpgsqlCommand command) { }
        public void LogFailure(NpgsqlCommand command, Exception ex) { }
        public void LogSuccess(NpgsqlBatch batch) { }
        public void LogFailure(NpgsqlBatch batch, Exception ex) { }
        public void LogFailure(Exception ex, string message) { }
        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit) { }
    }

    private void anAsyncSnapshot()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
            opts.Events.UseIdentityMapForAggregates = false;
        });
    }

    private async Task<Guid> appendThreeEventStreamAsync()
    {
        var id = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<SimpleAggregate>(id, new AEvent(), new BEvent(), new CEvent());
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private async Task catchTheDaemonUpAsync()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(15.Seconds());
        await daemon.StopAllAsync();
    }

    [Fact]
    public async Task aggregate_is_not_null_when_the_daemon_writes_the_first_snapshot_mid_fetch()
    {
        anAsyncSnapshot();
        var id = await appendThreeEventStreamAsync();

        await using var session = theStore.LightweightSession();
        session.Logger = new DelaySnapshotRead(2);

        // Not awaited yet: the batch goes out and the document read starts sleeping, inside the
        // session transaction that already holds the mt_streams row lock.
        var fetch = session.Events.FetchForExclusiveWriting<SimpleAggregate>(id);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // The daemon commits the first snapshot for this stream while that read is still open. It can,
        // because the row lock is on mt_streams and the daemon writes the snapshot table.
        await catchTheDaemonUpAsync();

        var stream = await fetch;

        stream.CurrentVersion.ShouldBe(3);

        // On the unfixed code this is null: the document read saw no snapshot, and the events read --
        // taking its own later snapshot inside the same READ COMMITTED transaction -- found the
        // daemon's brand-new mt_version 3 and excluded every event behind it.
        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.ACount.ShouldBe(1);
        stream.Aggregate.BCount.ShouldBe(1);
        stream.Aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task aggregate_is_not_stale_when_the_daemon_advances_the_snapshot_mid_fetch()
    {
        anAsyncSnapshot();

        // One event, snapshotted, so the fetch below finds a STALE document rather than none at all.
        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<SimpleAggregate>(id, new AEvent());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await catchTheDaemonUpAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(id, new BEvent(), new CEvent());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var fetchSession = theStore.LightweightSession();
        fetchSession.Logger = new DelaySnapshotRead(2);

        var fetch = fetchSession.Events.FetchForExclusiveWriting<SimpleAggregate>(id);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        await catchTheDaemonUpAsync();

        var stream = await fetch;

        stream.CurrentVersion.ShouldBe(3);
        stream.Aggregate.ShouldNotBeNull();

        // On the unfixed code this is the version-1 document: the events read excluded B and C because
        // by then mt_version was already 3. Silent -- and SaveChangesAsync would accept the result.
        stream.Aggregate.ACount.ShouldBe(1);
        stream.Aggregate.BCount.ShouldBe(1);
        stream.Aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task the_batched_exclusive_fetch_refuses_rather_than_answering_wrongly()
    {
        // A batch handler is handed one reader and cannot issue another statement, so it has no redo
        // available. The choice there is a wrong aggregate or an exception, and for a fetch whose whole
        // purpose is to decide what to write, the exception is the better answer. On the unfixed code this
        // returned a null Aggregate for a perfectly healthy stream.
        anAsyncSnapshot();
        var id = await appendThreeEventStreamAsync();

        await using var session = theStore.LightweightSession();
        session.Logger = new DelaySnapshotRead(2);

        var batch = session.CreateBatchQuery();
        var query = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(id);
        var execute = batch.Execute();

        await Task.Delay(500, TestContext.Current.CancellationToken);
        await catchTheDaemonUpAsync();

        // It surfaces from Execute(), which is where the reader is consumed.
        var ex = await Should.ThrowAsync<SnapshotRaceException>(async () => await execute);
        ex.AggregateType.ShouldBe(typeof(SimpleAggregate));
        ex.Id.ShouldBe(id);
        ex.Message.ShouldContain("Retry the batch");

        // Deliberately NOT awaiting `query`. When Execute() faults it never completes the individual
        // BatchQueryItem tasks, and BatchedQuery.FetchForExclusiveWriting ends in `return await
        // resultTask`, so that task is orphaned and awaiting it blocks forever. Doing so here hung a test
        // run for eight hours. That dangling-task behaviour is pre-existing for any batched handler that
        // throws, not something this fix introduced, but it is the reason this assertion stops at Execute.
        query.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task a_batched_exclusive_fetch_is_untouched_when_nothing_races_it()
    {
        // The control for the above: no daemon write in the window, no exception, same answer as always.
        anAsyncSnapshot();
        var id = await appendThreeEventStreamAsync();

        await using var session = theStore.LightweightSession();
        var batch = session.CreateBatchQuery();
        var query = batch.Events.FetchForExclusiveWriting<SimpleAggregate>(id);
        await batch.Execute(TestContext.Current.CancellationToken);

        var stream = await query;
        stream.CurrentVersion.ShouldBe(3);
        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.ACount.ShouldBe(1);
        stream.Aggregate.BCount.ShouldBe(1);
        stream.Aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task the_exclusive_fetch_still_holds_its_stream_lock()
    {
        // The control that matters most: whatever the fix does to the reads, the exclusive fetch must
        // still hold the mt_streams row lock until SaveChangesAsync. A second exclusive fetch of the
        // same stream from another session has to block rather than proceed.
        anAsyncSnapshot();
        var id = await appendThreeEventStreamAsync();

        await using var first = theStore.LightweightSession();
        var held = await first.Events.FetchForExclusiveWriting<SimpleAggregate>(id);
        held.AppendOne(new DEvent());

        await using var second = theStore.LightweightSession();
        var blocked = second.Events.FetchForExclusiveWriting<SimpleAggregate>(id);

        // Still waiting on the lock a moment later, rather than having sailed past it.
        await Task.Delay(750, TestContext.Current.CancellationToken);
        blocked.IsCompleted.ShouldBeFalse();

        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        var secondStream = await blocked;
        secondStream.Aggregate.ShouldNotBeNull();
        secondStream.Aggregate.DCount.ShouldBe(1);
    }

}
