#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using Marten;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Storage;
using Xunit;

namespace CoreTests.Bugs;

/// <summary>
///     #5914, below the event store: every operation here translates the same SQLSTATE, so which one answers is
///     exactly which one the batch attributed the failure to. Each shape puts the failing statement at a
///     different reader position -- behind a result set (surfaces from <c>NextResultAsync</c>), in a run of
///     statements with none (surfaces from <c>NextResultAsync</c> or from opening the reader), first, last.
/// </summary>
public class Bug_5914_each_operation_translates_its_own_statement: OneOffConfigurationsContext
{
    private const string Fails = "select 1/0";
    private const string FailsWithoutResult = "do $$ begin perform 1/0; end $$";
    private const string Succeeds = "select 1";
    private const string SucceedsWithoutResult = "do $$ begin perform 1; end $$";

    // sticky: the session is pinned to one connection by BeginTransactionAsync(), which is what
    // UseStickyConnectionLifetimes did before 10.0 (TransactionalConnection)
    private async Task<IDocumentSession> OpenSessionAsync(bool sticky, bool applicationTransform = false)
    {
        StoreOptions(opts =>
        {
            if (applicationTransform)
            {
                opts.Advanced.ExceptionTransforms.IfExceptionIs<PostgresException>()
                    .If(e => e.SqlState == PostgresErrorCodes.DivisionByZero)
                    .ThenTransformTo(_ => new ClaimedException("application"));
            }
        });
        var session = theStore.LightweightSession();
        _disposables.Add(session);
        if (sticky) await session.BeginTransactionAsync(CancellationToken.None);
        return session;
    }

    private static async Task<string> ClaimedByAsync(IDocumentSession session, params IStorageOperation[] operations)
    {
        foreach (var operation in operations) session.QueueOperation(operation);
        return (await Should.ThrowAsync<ClaimedException>(() => session.SaveChangesAsync())).ClaimedBy;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_statement_behind_a_result_set_is_its_own_operations(bool sticky)
    {
        // The issue's shape: operation 0 is postprocessed, then advancing the reader raises operation 1's error
        (await ClaimedByAsync(await OpenSessionAsync(sticky),
                new Claiming("A", Succeeds), new Claiming("B", Fails), new Claiming("C", Succeeds)))
            .ShouldBe("B");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_failing_first_statement_is_its_own_operations(bool sticky)
    {
        (await ClaimedByAsync(await OpenSessionAsync(sticky),
                new Claiming("A", Fails), new Claiming("B", Succeeds)))
            .ShouldBe("A");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_failing_last_statement_without_a_result_set_is_its_own_operations(bool sticky)
    {
        (await ClaimedByAsync(await OpenSessionAsync(sticky),
                new Claiming("A", Succeeds), new ClaimingNoData("B", FailsWithoutResult)))
            .ShouldBe("B");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_failure_inside_a_run_without_result_sets_behind_a_result_set(bool sticky)
    {
        // Advancing past A executes B, C and D together; C failed
        (await ClaimedByAsync(await OpenSessionAsync(sticky),
                new Claiming("A", Succeeds), new ClaimingNoData("B", SucceedsWithoutResult),
                new ClaimingNoData("C", FailsWithoutResult), new Claiming("D", Succeeds)))
            .ShouldBe("C");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_failure_inside_a_run_without_result_sets_while_opening_the_reader(bool sticky)
    {
        // Opening the reader executes A, B and C together; B failed. Every operation used to be offered it in
        // order, so A answered.
        (await ClaimedByAsync(await OpenSessionAsync(sticky),
                new ClaimingNoData("A", SucceedsWithoutResult), new ClaimingNoData("B", FailsWithoutResult),
                new Claiming("C", Succeeds), new Claiming("D", Succeeds)))
            .ShouldBe("B");
    }

    [Fact]
    public async Task an_operation_that_does_not_translate_its_own_failure_leaves_it_to_marten()
    {
        // Attributed to B, which declines: A must not get it as a second choice
        var ex = await Should.ThrowAsync<MartenCommandException>(async () =>
        {
            var session = await OpenSessionAsync(false);
            session.QueueOperation(new Claiming("A", Succeeds));
            session.QueueOperation(new Claiming("B", Fails) { Declines = true });
            await session.SaveChangesAsync();
        });

        ex.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.DivisionByZero);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task the_failing_operation_translates_before_the_applications_transforms(bool sticky)
    {
        // #5682's documented order: the operation's own transform, then the application's, then Marten's. A
        // sticky (TransactionalConnection) session opened the reader through its own ExecuteReaderAsync, which
        // ran the application's transforms first, so a first statement's failure never reached its operation.
        (await ClaimedByAsync(await OpenSessionAsync(sticky, applicationTransform: true),
                new Claiming("A", Fails), new Claiming("B", Succeeds)))
            .ShouldBe("A");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task what_the_failing_operation_declines_goes_to_the_application_not_to_another_operation(bool sticky)
    {
        (await ClaimedByAsync(await OpenSessionAsync(sticky, applicationTransform: true),
                new Claiming("A", Succeeds), new Claiming("B", Fails) { Declines = true }))
            .ShouldBe("application");
    }

    [Fact]
    public async Task a_re_executed_batch_does_not_trust_completion_left_over_from_its_last_run()
    {
        // Npgsql never resets NpgsqlBatchCommand.StatementType, and the resilience pipeline re-executes the
        // very same batch. Run 1 fails at B, so A is recorded complete; run 2 fails at A, and the stale record
        // would name B. Run 2 must fall back to statement order inside the window instead.
        await using var session = theStore.LightweightSession();
        var operations = new IStorageOperation[]
        {
            new ClaimingNoData("A", FailsWhen("a")), new ClaimingNoData("B", FailsWhen("b")),
            new Claiming("C", Succeeds)
        };
        var page = new OperationPage((IMartenSession)session, operations);
        var batch = page.Compile();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        batch.Connection = conn;

        async Task<string> runAsync(string failing)
        {
            await conn.CreateCommand($"set bug5914.fail = '{failing}'").ExecuteNonQueryAsync();
            var e = await Should.ThrowAsync<PostgresException>(() => batch.ExecuteReaderAsync());
            page.TryTransformExecuteFailure(e, out var transformed).ShouldBeTrue();
            return transformed.ShouldBeOfType<ClaimedException>().ClaimedBy;
        }

        (await runAsync("b")).ShouldBe("B");

        page.Compile().ShouldBeSameAs(batch);
        (await runAsync("a")).ShouldBe("A");
    }

    private static string FailsWhen(string name) =>
        $"do $$ begin if current_setting('bug5914.fail') = '{name}' then perform 1/0; end if; end $$";

    public class ClaimedException(string claimedBy): Exception($"Claimed by {claimedBy}")
    {
        public string ClaimedBy { get; } = claimedBy;
    }

    public class Claiming(string name, string sql): IStorageOperation, IExceptionTransform
    {
        public bool Declines { get; init; }

        public void ConfigureCommand(Weasel.Postgresql.ICommandBuilder builder, IStorageSession session) => builder.Append(sql);

        // No document type, as retry_mechanism's fake: the session has nothing to resolve storage for
        public Type DocumentType => null!;

        public OperationRole Role() => OperationRole.Other;

        public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            while (await reader.ReadAsync(token))
            {
            }
        }

        public bool TryTransform(Exception original, [NotNullWhen(true)] out Exception? transformed)
        {
            var pg = original as PostgresException ?? original.InnerException as PostgresException;
            if (!Declines && pg is { SqlState: PostgresErrorCodes.DivisionByZero })
            {
                transformed = new ClaimedException(name);
                return true;
            }

            transformed = null;
            return false;
        }

        public override string ToString() => name;
    }

    public class ClaimingNoData(string name, string sql): Claiming(name, sql), NoDataReturnedCall;
}
