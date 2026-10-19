#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Projections;
using Marten;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

public record Bug5688Started;

public record Bug5688Incremented;

public class Bug5688Doc
{
    public Guid Id { get; set; }
}

public class Bug5688Counter
{
    public Guid Id { get; set; }
    public int Count { get; set; }
    public static Bug5688Counter Create(Bug5688Started _) => new();
    public void Apply(Bug5688Incremented _) => Count++;
}

/// <summary>
/// #5688: a <c>SaveChangesAsync</c> whose token is cancelled while no statement is executing -- after the
/// batch's statements ran but before <c>COMMIT</c> -- left its transaction <c>idle in transaction</c>, still
/// holding the stream's row lock and the uncommitted <c>mt_events</c> rows for the sequence numbers it drew.
///
/// <para>
/// Two things combine. <c>TransactionalConnection.ExecuteBatchPagesAsync</c> rolls back with the caller's
/// (already cancelled) token, so Npgsql throws without ever sending <c>ROLLBACK</c>, and its participants and
/// commit sit outside the <c>try</c> so a cancellation there is not rolled back at all. Then the save's
/// <c>catch</c> awaits the tombstone write inline, on a fresh connection, at the very sequence numbers the
/// still-open transaction holds -- so the save waits on itself until the command timeout breaks the wait,
/// and every other writer of the stream waits with it.
/// </para>
///
/// <para>
/// The command timeout is lowered to 10s so that the unfixed behaviour fails these tests in seconds rather
/// than the default 30s; the assertions only allow a small fraction of it.
/// </para>
/// </summary>
public class Bug_5688_cancelled_save_leaves_transaction_open: OneOffConfigurationsContext
{
    private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(3);

    private readonly ITestOutputHelper _output;

    public Bug_5688_cancelled_save_leaves_transaction_open(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Cancels the caller's token once every statement has run, as a disconnecting client would.</summary>
    private sealed class CancelBeforeCommit(CancellationTokenSource cts): ITransactionParticipant
    {
        public Task BeforeCommitAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            CancellationToken token)
        {
            cts.Cancel();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Cancels the caller's token while results are being read back, and observes it the way any
    /// <c>reader.ReadAsync(token)</c> would. That lands inside the <c>try</c> of
    /// <c>ExecuteBatchPagesAsync</c>, so this is the path that goes through <c>RollbackAsync(token)</c>.
    /// </summary>
    private sealed class CancelWhileReadingResults(CancellationTokenSource cts): IStorageOperation
    {
        public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
        {
            builder.Append("select 1");
        }

        public Type DocumentType => typeof(Bug5688Counter);

        public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public OperationRole Role() => OperationRole.Other;
    }

    private async Task<Guid> startStreamAsync()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<Bug5688Counter>(SnapshotLifecycle.Inline);
            opts.Schema.For<Bug5688Doc>().UseOptimisticConcurrency(true);
            opts.CommandTimeout = 10;
        });

        var id = Guid.NewGuid();
        await using var setup = theStore.LightweightSession();
        setup.Events.StartStream<Bug5688Counter>(id, new Bug5688Started());
        await setup.SaveChangesAsync();

        return id;
    }

    private async Task<string?> backendStateAsync(int pid)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select state from pg_stat_activity where pid = $1", conn)
        {
            Parameters = { new NpgsqlParameter { Value = pid } }
        };
        return (string?)await cmd.ExecuteScalarAsync();
    }

    /// <summary>
    /// Whether some transaction still holds the stream row's <c>for update</c> lock, asked from a separate
    /// connection with <c>nowait</c> so the answer is immediate.
    /// </summary>
    /// <remarks>
    /// Deliberately not "is the cancelled session's backend idle in transaction". Once the session rolls
    /// back, its connection goes back to the pool, and the background tombstone write can pick up that same
    /// physical connection -- same pid -- and be mid-transaction of its own at the moment of asking.
    /// </remarks>
    private async Task<bool> streamRowIsLockedAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"select version from {theStore.Options.Events.DatabaseSchemaName}.mt_streams where id = $1 for update nowait",
            conn)
        {
            Parameters = { new NpgsqlParameter { Value = id } }
        };

        try
        {
            await cmd.ExecuteScalarAsync();
            return false;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            return true;
        }
    }

    private async Task<long> tombstoneCountAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"select count(*) from {theStore.Options.Events.DatabaseSchemaName}.mt_events where type = 'tombstone'",
            conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Runs one cancelled save of <paramref name="id" /> and then asserts on everything the issue expects:
    /// a prompt <see cref="OperationCanceledException" />, a transaction that has actually ended before the
    /// session is disposed, and a second writer of the same stream that is not held up by it.
    /// </summary>
    private async Task assertCancelledSaveReleasesTheStreamAsync(IDocumentSession session, Guid id,
        CancellationTokenSource cts)
    {
        var clock = Stopwatch.StartNew();
        Exception? thrown = null;
        try
        {
            await session.SaveChangesAsync(cts.Token);
        }
        catch (Exception e)
        {
            thrown = e;
        }

        var saveTook = clock.Elapsed;
        _output.WriteLine($"cancelled save threw {thrown?.GetType().Name ?? "nothing"} after {saveTook.TotalSeconds:F2}s");

        // The heart of it: a transaction neither committed nor rolled back keeps the stream row locked
        // until the session is disposed. The session is still alive here.
        var locked = await streamRowIsLockedAsync(id);
        _output.WriteLine($"stream row {(locked ? "is still locked" : "is free")} before the cancelled session is disposed");
        locked.ShouldBeFalse();

        // A second writer of the same stream, while the cancelled session is still alive. It did nothing
        // wrong, and should be held up only as long as the cancelled transaction actually ran.
        clock.Restart();
        Exception? secondFailure = null;
        try
        {
            await using var other = theStore.LightweightSession();
            var stream = await other.Events.FetchForExclusiveWriting<Bug5688Counter>(id);
            stream.AppendOne(new Bug5688Incremented());
            await other.SaveChangesAsync();
        }
        catch (Exception e)
        {
            secondFailure = e;
        }

        var secondTook = clock.Elapsed;
        _output.WriteLine(
            $"second writer {(secondFailure == null ? "committed" : $"threw {secondFailure.GetType().Name}")} after {secondTook.TotalSeconds:F2}s");

        thrown.ShouldBeAssignableTo<OperationCanceledException>();
        saveTook.ShouldBeLessThan(Prompt);

        secondFailure.ShouldBeNull();
        secondTook.ShouldBeLessThan(Prompt);

        // The cancelled save drew sequence numbers it never committed; the tombstones that fill them
        // have to land once its transaction is gone rather than being lost to a lock wait. They are
        // written off the failed save's path, so wait for that write rather than assuming it happened.
        await theStore.Options.EventGraph.WaitForPendingTombstonesAsync();
        (await tombstoneCountAsync()).ShouldBeGreaterThan(0);

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamStateAsync(id))!.Version.ShouldBe(2);
    }

    [Fact]
    public async Task cancelled_before_commit_on_an_exclusive_writing_session()
    {
        // The reported shape: FetchForExclusiveWriting moves the session onto a TransactionalConnection
        // holding `select ... for update` on the stream row, and the token is cancelled in the window
        // between the last statement and COMMIT.
        var id = await startStreamAsync();

        using var cts = new CancellationTokenSource();
        await using var session = theStore.LightweightSession();
        ((ITransactionParticipantRegistrar)session).AddTransactionParticipant(new CancelBeforeCommit(cts));

        var stream = await session.Events.FetchForExclusiveWriting<Bug5688Counter>(id);
        stream.AppendOne(new Bug5688Incremented());
        await assertCancelledSaveReleasesTheStreamAsync(session, id, cts);
    }

    [Fact]
    public async Task cancelled_while_reading_results_on_an_exclusive_writing_session()
    {
        // Same session shape, but the cancellation is observed inside the try of ExecuteBatchPagesAsync,
        // so it is the RollbackAsync(token) in the catch -- not the commit -- that is handed a cancelled token.
        var id = await startStreamAsync();

        using var cts = new CancellationTokenSource();
        await using var session = theStore.LightweightSession();

        var stream = await session.Events.FetchForExclusiveWriting<Bug5688Counter>(id);
        stream.AppendOne(new Bug5688Incremented());
        session.QueueOperation(new CancelWhileReadingResults(cts));
        await assertCancelledSaveReleasesTheStreamAsync(session, id, cts);
    }

    [Fact]
    public async Task failed_save_on_a_caller_owned_transaction_does_not_wait_on_itself()
    {
        // The second half of #5688 does not need a cancellation at all. It needs a failure Marten detects
        // on the CLIENT while the transaction is still healthy -- a statement that fails on the server
        // aborts the transaction there and then, which releases its locks even before ROLLBACK arrives.
        // An optimistic-concurrency miss is exactly such a failure: the UPDATE matches no row, nothing
        // errors, and Marten raises ConcurrencyException from the result it reads back.
        //
        // With a caller-owned transaction Marten deliberately does not roll back -- the caller owns that --
        // so the appended event's row at its drawn sequence number stays uncommitted until the caller ends
        // the transaction. The tombstone write is still awaited inline, on another connection, at that
        // same sequence number, and so waits on the caller, who is waiting on Marten.
        var id = await startStreamAsync();

        var doc = new Bug5688Doc();
        await using (var setup = theStore.LightweightSession())
        {
            setup.Store(doc);
            await setup.SaveChangesAsync();
        }

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx));
        session.Events.Append(id, new Bug5688Incremented());
        session.UpdateExpectedVersion(doc, Guid.NewGuid());

        var clock = Stopwatch.StartNew();
        Exception? thrown = null;
        try
        {
            await session.SaveChangesAsync();
        }
        catch (Exception e)
        {
            thrown = e;
        }

        var saveTook = clock.Elapsed;
        _output.WriteLine($"failed save threw {thrown?.GetType().Name ?? "nothing"} after {saveTook.TotalSeconds:F2}s");
        _output.WriteLine($"caller's backend is '{await backendStateAsync(conn.ProcessID)}'");

        thrown.ShouldBeOfType<ConcurrencyException>();
        saveTook.ShouldBeLessThan(Prompt);

        // Once the caller does end its transaction, the sequence number is free and the tombstone lands.
        await tx.RollbackAsync();
        var deadline = Stopwatch.StartNew();
        while (await tombstoneCountAsync() == 0 && deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(100);
        }

        _output.WriteLine($"tombstones {deadline.Elapsed.TotalSeconds:F2}s after the caller rolled back: {await tombstoneCountAsync()}");
        (await tombstoneCountAsync()).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task cancelled_before_commit_on_the_default_lifetime()
    {
        // Control: an ordinary append goes through AutoClosingLifetime, which opens and closes its own
        // connection inside ExecuteBatchPagesAsync and closes it in a finally before the tombstones are
        // posted. It never held the stream row lock, so the lock check is trivially true here.
        var id = await startStreamAsync();

        using var cts = new CancellationTokenSource();
        await using var session = theStore.LightweightSession();
        ((ITransactionParticipantRegistrar)session).AddTransactionParticipant(new CancelBeforeCommit(cts));

        session.Events.Append(id, new Bug5688Incremented());

        await assertCancelledSaveReleasesTheStreamAsync(session, id, cts);
    }
}
