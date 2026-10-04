#nullable enable
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests.Bugs;

public class Bug5577Doc
{
    public Guid Id { get; set; }
    public int Value { get; set; }
}

/// <summary>
/// #5577 and #5578. A sticky connection lifetime -- UseStickyConnectionLifetimes, Serializable, a
/// user-supplied connection, ambient or external transactions -- holds one connection open for the life of
/// the session. Nothing in TransactionalConnection tracks that the connection or its transaction has become
/// unusable, so the read retry replays a command against state that cannot be restored.
/// </summary>
public class Bug_5577_sticky_lifetime_has_no_failure_state: OneOffConfigurationsContext
{
    private static async Task terminateBackend(int pid)
    {
        await using var killer = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await killer.OpenAsync();
        await using var cmd = new NpgsqlCommand("select pg_terminate_backend(:pid)", killer);
        cmd.Parameters.AddWithValue("pid", pid);
        await cmd.ExecuteScalarAsync();
    }

    /// <summary>
    /// #5577. The backend dies mid-session. EnsureConnectedAsync tests only ConnectionState.Closed, and a
    /// connection Npgsql has broken reports Closed (with FullState Broken), so it silently reopens onto a
    /// DIFFERENT backend. ApplyAsync then hands the command the stale NpgsqlTransaction, which Npgsql accepts
    /// without complaint -- and the query runs with no transaction at all.
    ///
    /// The session was Serializable precisely so that it would keep seeing its own snapshot. A value committed
    /// by somebody else after this transaction began must never be visible here.
    /// </summary>
    [Fact]
    public async Task a_retry_after_the_backend_dies_must_not_leave_the_transaction()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Bug5577Doc));

        var id = Guid.NewGuid();
        await using (var setup = theStore.LightweightSession())
        {
            setup.Store(new Bug5577Doc { Id = id, Value = 1 });
            await setup.SaveChangesAsync();
        }

        await using var session = theStore.QuerySession(new SessionOptions
        {
            IsolationLevel = IsolationLevel.Serializable
        });

        // establishes the connection and the serializable snapshot
        (await session.LoadAsync<Bug5577Doc>(id))!.Value.ShouldBe(1);
        var pid = session.Connection!.ProcessID;

        await using (var other = theStore.LightweightSession())
        {
            other.Store(new Bug5577Doc { Id = id, Value = 2 });
            await other.SaveChangesAsync();
        }

        // still inside the original snapshot, so the commit above is invisible
        (await session.LoadAsync<Bug5577Doc>(id))!.Value.ShouldBe(1);

        await terminateBackend(pid);

        // The read fails with 57P01 and the default read policy retries it. The retry must not be allowed to
        // reopen onto a new backend: the transaction died with the old one and no connection brings it back.
        int? valueAfterRetry = null;
        var thrown = await Record.ExceptionAsync(async () =>
        {
            valueAfterRetry = (await session.LoadAsync<Bug5577Doc>(id))!.Value;
        });

        valueAfterRetry.ShouldNotBe(2,
            "the retry silently left the serializable transaction and answered from a newer snapshot");

        thrown.ShouldBeOfType<SessionTransactionUnusableException>()
            .InnerException.ShouldNotBeNull("the original 57P01 should be reported as the cause");
    }

    /// <summary>
    /// #5578. A command timeout inside an open transaction leaves the connection Open/Open but the
    /// server-side transaction aborted. Nothing records that, so the next use of the session is answered by
    /// PostgreSQL with a bare 25P02 that names neither the timeout nor the way out.
    /// </summary>
    [Fact]
    public async Task a_timeout_must_not_leave_the_session_answering_bare_25P02()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Bug5577Doc));

        await using var session = theStore.QuerySession(new SessionOptions
        {
            IsolationLevel = IsolationLevel.Serializable, Timeout = 1
        });

        // open the transaction
        await session.Query<Bug5577Doc>().CountAsync(CancellationToken.None);

        var timeout = await Record.ExceptionAsync(() =>
            session.AdvancedSql.QueryAsync<int>("select 1 from pg_sleep(5)", CancellationToken.None));

        timeout.ShouldNotBeNull("expected the 1 second CommandTimeout to fire");

        var next = await Record.ExceptionAsync(() => session.Query<Bug5577Doc>().CountAsync(CancellationToken.None));

        findPostgresException(next)?.SqlState.ShouldNotBe(PostgresErrorCodes.InFailedSqlTransaction,
            "the session reported a bare 25P02 instead of naming the timeout that caused it");

        // and it names the timeout rather than the 25P02 the timeout provoked
        var unusable = next.ShouldBeOfType<SessionTransactionUnusableException>();
        findTimeoutException(unusable).ShouldNotBeNull("the original timeout should be reported as the cause");
    }

    private static TimeoutException? findTimeoutException(Exception? exception)
    {
        for (var e = exception; e != null; e = e.InnerException)
        {
            if (e is TimeoutException timeout) return timeout;
        }

        return null;
    }

    private static PostgresException? findPostgresException(Exception? exception)
    {
        for (var e = exception; e != null; e = e.InnerException)
        {
            if (e is PostgresException pg) return pg;
        }

        return null;
    }

    public Bug_5577_sticky_lifetime_has_no_failure_state()
    {
        StoreOptions(opts => opts.Schema.For<Bug5577Doc>());
    }
}
