using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// #5604, second half. <c>FetchLatest</c> and <c>StreamForReading</c> on an aggregate projected with
/// <c>ProjectionLifecycle.Async</c> read the snapshot document and then the events after it as two
/// separate statements, and carried the same <c>// TODO -- use read only transaction????</c> the
/// expected-version overload did. Under READ COMMITTED each statement takes its own snapshot, so a
/// daemon snapshot write landing between them makes the delta query — which re-reads
/// <c>a.mt_version</c> — exclude the very events the document read never saw. <c>FetchLatest</c> then
/// answers null for a live stream, or silently stale state.
///
/// <para>
/// The window is microseconds wide. It is widened deterministically here with the technique the
/// reporter of #5604 used: a session logger appends a <c>pg_sleep</c> to the document read, and the
/// daemon is run while that read is parked.
/// </para>
/// </summary>
public class Bug_5604_fetch_latest_needs_one_snapshot: OneOffConfigurationsContext
{
    /// <summary>
    /// Holds the snapshot document read open long enough for the daemon to commit a snapshot
    /// underneath it. Keyed off the SHAPE of the batch — a document load by id followed by an mt_events
    /// read — so it only ever stretches the read under test, and goes inert rather than silently
    /// delaying something else the day the batch stops being built this way.
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
        // UseIdentityMapForAggregates off: the identity-map shortcut in FetchForReading would answer
        // from memory and never reach the SQL under test.
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async);
            opts.Events.UseIdentityMapForAggregates = false;
            opts.Schema.For<User>();
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
    public async Task fetch_latest_is_not_null_when_the_daemon_writes_the_first_snapshot_mid_read()
    {
        anAsyncSnapshot();
        var id = await appendThreeEventStreamAsync();

        await using var session = theStore.LightweightSession();
        session.Logger = new DelaySnapshotRead(2);

        // Not awaited yet: the batch goes out and the document read starts sleeping.
        var fetch = session.Events.FetchLatest<SimpleAggregate>(id).AsTask();
        await Task.Delay(500, TestContext.Current.CancellationToken);

        await catchTheDaemonUpAsync();

        // On the unfixed code this is null: the document read saw nothing, and the events read -- taking
        // its own later snapshot -- found the daemon's brand-new mt_version 3 and excluded every event
        // behind it.
        var aggregate = await fetch;

        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task fetch_latest_is_not_stale_when_the_daemon_advances_the_snapshot_mid_read()
    {
        anAsyncSnapshot();

        // One event, snapshotted, so the read below finds a STALE document rather than none at all.
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

        await using var readSession = theStore.LightweightSession();
        readSession.Logger = new DelaySnapshotRead(2);

        var fetch = readSession.Events.FetchLatest<SimpleAggregate>(id).AsTask();
        await Task.Delay(500, TestContext.Current.CancellationToken);

        await catchTheDaemonUpAsync();

        var aggregate = await fetch;

        aggregate.ShouldNotBeNull();

        // On the unfixed code this is the version-1 document: the events read excluded B and C because
        // by then mt_version was already 3.
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task streaming_a_latest_aggregate_shares_one_snapshot_too()
    {
        anAsyncSnapshot();
        var id = await appendThreeEventStreamAsync();

        await using var session = theStore.LightweightSession();
        session.Logger = new DelaySnapshotRead(2);

        var destination = new MemoryStream();
        var stream = session.Events.StreamLatestJson<SimpleAggregate>(id, destination);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        await catchTheDaemonUpAsync();

        (await stream).ShouldBeTrue();

        destination.Position = 0;
        var aggregate = theStore.Options.Serializer().FromJson<SimpleAggregate>(destination);
        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task the_batched_fetch_latest_shares_one_snapshot_too()
    {
        anAsyncSnapshot();
        var id = await appendThreeEventStreamAsync();

        await using var session = theStore.LightweightSession();
        session.Logger = new DelaySnapshotRead(2);

        var batch = session.CreateBatchQuery();
        var query = batch.Events.FetchLatest<SimpleAggregate>(id);
        var execute = batch.Execute();

        await Task.Delay(500, TestContext.Current.CancellationToken);
        await catchTheDaemonUpAsync();
        await execute;

        var aggregate = await query;
        aggregate.ShouldNotBeNull();
        aggregate.ACount.ShouldBe(1);
        aggregate.BCount.ShouldBe(1);
        aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task a_batched_fetch_latest_has_to_be_first_in_its_batch()
    {
        // The cost, and a deliberate BEHAVIOUR CHANGE: PostgreSQL accepts
        // `begin transaction isolation level …` only as a transaction's first statement, so a batched
        // FetchLatest for an Async aggregate inherits the #5535 must-be-first rule that FetchForWriting
        // already had. Refused at the enlisting call, naming the call to move, rather than as a bare
        // 25001 out of Execute().
        anAsyncSnapshot();
        var id = await appendThreeEventStreamAsync();

        await using var session = theStore.LightweightSession();
        var batch = session.CreateBatchQuery();
        batch.Load<User>(Guid.NewGuid());

        var ex = Should.Throw<InvalidOperationException>(() => batch.Events.FetchLatest<SimpleAggregate>(id));

        ex.Message.ShouldContain("has to be the FIRST operation in its batch");
        ex.Message.ShouldContain("FetchLatest");
        ex.Message.ShouldContain("SimpleAggregate");
    }

    [Fact]
    public async Task a_batched_fetch_latest_cannot_share_a_batch_with_an_exclusive_fetch()
    {
        // The other rule that comes with the bracket: its `end` would commit the session transaction
        // holding the exclusive fetch's row lock. Message must name FetchLatest, not FetchForWriting.
        anAsyncSnapshot();
        var id = await appendThreeEventStreamAsync();

        await using var session = theStore.LightweightSession();
        var batch = session.CreateBatchQuery();
        batch.Events.FetchLatest<SimpleAggregate>(id);

        var ex = Should.Throw<InvalidOperationException>(() =>
            batch.Events.FetchForExclusiveWriting<SimpleAggregate>(Guid.NewGuid()));

        ex.Message.ShouldContain("FetchForExclusiveWriting cannot share a batch with FetchLatest");
    }

    [Fact]
    public async Task a_batched_fetch_latest_inside_a_caller_owned_transaction_need_not_be_first()
    {
        // #5611's guard suppresses the bracket inside a caller-owned transaction, so neither rule
        // applies there -- the same relief FetchForWriting got.
        anAsyncSnapshot();
        var id = await appendThreeEventStreamAsync();

        var ct = TestContext.Current.CancellationToken;
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx));

        var batch = session.CreateBatchQuery();
        batch.Load<User>(Guid.NewGuid());
        var query = batch.Events.FetchLatest<SimpleAggregate>(id);
        await batch.Execute(ct);

        (await query).ShouldNotBeNull();

        await tx.RollbackAsync(ct);
    }

    [Fact]
    public async Task fetch_latest_does_not_commit_a_caller_owned_transaction()
    {
        // The #5611 hazard, now that FetchLatest emits the bracket too: it must not commit the caller's
        // transaction. Without the guard this test loses the rollback exactly as FetchForWriting did.
        anAsyncSnapshot();
        var id = await appendThreeEventStreamAsync();

        var ct = TestContext.Current.CancellationToken;
        var marker = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx)))
        {
            await session.Events.FetchLatest<SimpleAggregate>(id);

            session.Store(new User { Id = marker, FirstName = "should be rolled back" });
            await session.SaveChangesAsync(ct);
        }

        await tx.RollbackAsync(ct);

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<User>(marker, ct)).ShouldBeNull();
    }
}
