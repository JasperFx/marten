using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Core;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// #5604: for an aggregate projected with <c>ProjectionLifecycle.Async</c>,
/// <c>FetchForWriting(id, expectedVersion)</c> runs its three reads — stream version, snapshot
/// document, events after the snapshot — WITHOUT a shared snapshot. The non-version overload wraps
/// exactly the same three reads in <c>begin transaction isolation level repeatable read read only
/// … end</c>; the expected-version path in <c>FetchAsyncPlan.ExpectedVersion.cs</c> does not, and
/// carries a <c>// TODO -- use read only transaction????</c> at precisely that spot.
///
/// <para>
/// Under READ COMMITTED each statement in the batch takes its own snapshot. If the async daemon
/// commits the snapshot document between the snapshot read and the events read, the events read
/// already sees the new <c>mt_version</c> and its delta join (<c>where a.mt_version is NULL or
/// d.version &gt; a.mt_version</c>) therefore excludes the very events the snapshot read never saw.
/// Two outcomes, both wrong:
/// </para>
///
/// <list type="bullet">
/// <item><b>No previous snapshot:</b> the snapshot read returns nothing and the events read returns
/// nothing, because the brand-new snapshot already covers them. <c>Aggregate</c> comes back null for
/// a stream that exists, with <c>StartingVersion</c> above zero.</item>
/// <item><b>Stale previous snapshot:</b> the snapshot read returns the old state and the events read
/// returns nothing past the new snapshot. The aggregate comes back at the old version while
/// <c>StartingVersion</c> reports the current one — so the command decides on stale state and
/// <c>SaveChangesAsync</c> still succeeds, because the expected version matches. Silent.</item>
/// </list>
///
/// <para>
/// The window is microseconds wide, which is why the reporter saw it only intermittently and under
/// load. It is widened here deterministically: a session logger appends a <c>pg_sleep</c> to the
/// snapshot read, and the daemon is started while that read is sleeping. Wolverine's
/// <c>[WriteAggregate]</c> with a <c>Version</c> member on the command reaches the same overload,
/// where the null case surfaced as an HTTP 500.
/// </para>
/// </summary>
public class Bug_5604_expected_version_fetch_needs_one_snapshot: OneOffConfigurationsContext
{
    /// <summary>
    /// Holds the snapshot read open long enough for the daemon to commit a snapshot underneath it.
    /// The predicate deliberately keys off the SHAPE of the batch — a document load by id followed
    /// by an mt_events read — so it only ever stretches the fetch under test, and goes inert the day
    /// the batch stops being built that way rather than silently delaying something else.
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

    private async Task<Guid> appendThreeEventStreamAsync()
    {
        var id = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<SimpleAggregate>(id, new AEvent(), new BEvent(), new CEvent());
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    /// <summary>
    /// Runs the daemon far enough to write the snapshot, while the fetch under test is parked inside
    /// its snapshot read.
    /// </summary>
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
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async));

        var id = await appendThreeEventStreamAsync();

        await using var session = theStore.LightweightSession();
        session.Logger = new DelaySnapshotRead(2);

        // Not awaited yet: the batch goes out and the snapshot read starts sleeping.
        var fetch = session.Events.FetchForWriting<SimpleAggregate>(id, 3);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // The daemon commits the first snapshot for this stream while that read is still open.
        await catchTheDaemonUpAsync();

        var stream = await fetch;

        stream.StartingVersion.ShouldBe(3);

        // On the unfixed code this is null: the snapshot read saw no document, and the events read —
        // taking its own later snapshot — found the daemon's brand-new mt_version 3 and excluded
        // every event behind it.
        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.ACount.ShouldBe(1);
        stream.Aggregate.BCount.ShouldBe(1);
        stream.Aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task aggregate_is_not_stale_when_the_daemon_advances_the_snapshot_mid_fetch()
    {
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async));

        // One event, then let the daemon write a snapshot at version 1 so the fetch below has a
        // STALE document to find rather than nothing at all.
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

        var fetch = fetchSession.Events.FetchForWriting<SimpleAggregate>(id, 3);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // The daemon catches the snapshot up from 1 to 3 while the snapshot read is still sleeping.
        await catchTheDaemonUpAsync();

        var stream = await fetch;

        stream.StartingVersion.ShouldBe(3);
        stream.Aggregate.ShouldNotBeNull();

        // On the unfixed code the aggregate is the version-1 document: the snapshot read returned it
        // and the events read excluded B and C, because by then mt_version was already 3. The stream
        // says 3 and the aggregate holds version 1's state -- and SaveChanges would accept it,
        // because the expected version does match.
        stream.Aggregate.ACount.ShouldBe(1);
        stream.Aggregate.BCount.ShouldBe(1);
        stream.Aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task the_batched_expected_version_fetch_shares_one_snapshot_too()
    {
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async));

        var id = await appendThreeEventStreamAsync();

        await using var session = theStore.LightweightSession();
        session.Logger = new DelaySnapshotRead(2);

        var batch = session.CreateBatchQuery();
        var query = batch.Events.FetchForWriting<SimpleAggregate>(id, 3);
        var execute = batch.Execute();

        await Task.Delay(500, TestContext.Current.CancellationToken);
        await catchTheDaemonUpAsync();
        await execute;

        var stream = await query;
        stream.StartingVersion.ShouldBe(3);
        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.ACount.ShouldBe(1);
        stream.Aggregate.BCount.ShouldBe(1);
        stream.Aggregate.CCount.ShouldBe(1);
    }

    [Fact]
    public async Task a_batched_expected_version_fetch_has_to_be_first_in_its_batch()
    {
        // The cost of the bracket, and a deliberate behaviour change: PostgreSQL accepts 'begin
        // transaction isolation level …' only as a transaction's first statement, so this overload
        // inherits the #5535 must-be-first rule its non-version sibling already had. Refused at the
        // enlisting call, where the caller can see which call to move, rather than as a bare 25001
        // out of Execute().
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async));

        var id = await appendThreeEventStreamAsync();

        await using var session = theStore.LightweightSession();
        var batch = session.CreateBatchQuery();
        batch.Load<SimpleAggregate>(Guid.NewGuid());

        var ex = Should.Throw<InvalidOperationException>(() =>
            batch.Events.FetchForWriting<SimpleAggregate>(id, 3));

        ex.Message.ShouldContain("has to be the FIRST operation in its batch");
        ex.Message.ShouldContain("SimpleAggregate");
    }

    [Fact]
    public async Task the_non_version_overload_is_already_correct()
    {
        // The control. Identical interleaving through FetchForWriting(id), which does wrap its reads
        // in one repeatable-read read-only transaction. This must pass before and after the fix --
        // it is the behaviour the expected-version path is being held to.
        StoreOptions(opts => opts.Projections.Snapshot<SimpleAggregate>(SnapshotLifecycle.Async));

        var id = await appendThreeEventStreamAsync();

        await using var session = theStore.LightweightSession();
        session.Logger = new DelaySnapshotRead(2);

        var fetch = session.Events.FetchForWriting<SimpleAggregate>(id);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        await catchTheDaemonUpAsync();

        var stream = await fetch;

        stream.StartingVersion.ShouldBe(3);
        stream.Aggregate.ShouldNotBeNull();
        stream.Aggregate.ACount.ShouldBe(1);
        stream.Aggregate.BCount.ShouldBe(1);
        stream.Aggregate.CCount.ShouldBe(1);
    }
}
