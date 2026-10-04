#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace CoreTests;

public class Bug5583Doc
{
    public Guid Id { get; set; }
    public int Value { get; set; }
}

/// <summary>
/// #5583, the follow-up to #5577/#5578. Those gave <c>TransactionalConnection</c> a failure state;
/// <c>ExternalTransaction</c> and <c>AmbientTransactionLifetime</c> had the same gap and were deliberately
/// left out, because their transactions are owned by the caller.
///
/// The resolution is the narrow one: Marten records the first failure and names it on the next use, and
/// does NOT roll anything back — the caller still owns that. What changes is that the session stops
/// answering a bare 25P02 that identifies neither the original failure nor who can resolve it.
/// </summary>
public class Bug_5583_caller_owned_transaction_failure_state: OneOffConfigurationsContext
{
    [Fact]
    public async Task an_external_transaction_names_the_original_failure_not_the_25P02()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Bug5583Doc));

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx));

        // Poison the caller's transaction with a failure that is nothing to do with Marten's next query.
        var first = await Record.ExceptionAsync(() =>
            session.QueryAsync<Bug5583Doc>("select * from a_table_that_does_not_exist"));
        first.ShouldNotBeNull();

        // Everything after this is answered 25P02 by PostgreSQL until the block ends.
        var next = await Record.ExceptionAsync(() => session.Query<Bug5583Doc>().CountAsync(CancellationToken.None));

        findPostgres(next)?.SqlState.ShouldNotBe(PostgresErrorCodes.InFailedSqlTransaction,
            "the session reported a bare 25P02 instead of naming the failure that caused it");

        var unusable = next.ShouldBeOfType<SessionTransactionUnusableException>();
        findPostgres(unusable)?.SqlState.ShouldBe(PostgresErrorCodes.UndefinedTable,
            "the original 42P01 should be reported as the cause");
    }

    /// <summary>
    /// Marten must not resolve a transaction it does not own. The caller's transaction is still theirs to
    /// roll back after the session has given up on it.
    /// </summary>
    [Fact]
    public async Task marten_does_not_roll_back_a_caller_owned_transaction()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var session = theStore.LightweightSession(SessionOptions.ForTransaction(tx)))
        {
            await Record.ExceptionAsync(() =>
                session.QueryAsync<Bug5583Doc>("select * from a_table_that_does_not_exist"));
            await Record.ExceptionAsync(() => session.Query<Bug5583Doc>().CountAsync(CancellationToken.None));
        }

        // still the caller's to resolve, and still resolvable -- Marten neither rolled it back nor
        // disposed it on the way out
        await Should.NotThrowAsync(async () => await tx.RollbackAsync());
    }

    private static PostgresException? findPostgres(Exception? exception)
    {
        for (var e = exception; e != null; e = e.InnerException)
        {
            if (e is PostgresException pg) return pg;
        }

        return null;
    }

    public Bug_5583_caller_owned_transaction_failure_state()
    {
        StoreOptions(opts => opts.Schema.For<Bug5583Doc>());
    }
}
