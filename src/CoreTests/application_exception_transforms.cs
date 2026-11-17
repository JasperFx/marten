using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Linq.MatchesSql;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests;

public class GuardRefusedException: Exception
{
    public GuardRefusedException(PostgresException inner): base("The guard refused the write", inner)
    {
    }
}

/// <summary>
/// #5682. An application registers its own exception transforms on
/// <see cref="AdvancedOptions.ExceptionTransforms" />, consulted before Marten's built-ins, so a
/// <see cref="PostgresException" /> with the application's own SQLSTATE can surface as a typed exception instead
/// of a <see cref="MartenCommandException" />.
/// </summary>
public class application_exception_transforms: OneOffConfigurationsContext
{
    private const string GuardState = "CT001";

    private string Guard => $"{SchemaName}.ct_guard()";

    private async Task UseGuardAsync(bool registerTransform = true)
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>();

            if (registerTransform)
            {
                opts.Advanced.ExceptionTransforms.IfExceptionIs<PostgresException>()
                    .If(e => e.SqlState == GuardState)
                    .ThenTransformTo(e => new GuardRefusedException(e));
            }
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();
        await conn.CreateCommand($"""
            create or replace function {Guard} returns int as $$
            begin
                raise exception 'refused by the guard' using errcode = '{GuardState}';
            end;
            $$ language plpgsql;
            """).ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task a_guard_queued_in_the_committing_batch_surfaces_as_the_applications_exception()
    {
        // The issue's own case: a guard queued with QueueSqlCommand, refusing inside the commit
        await UseGuardAsync();

        await using var session = theStore.LightweightSession();
        session.Store(Target.Random());
        session.QueueSqlCommand($"select {Guard}");

        var ex = await Should.ThrowAsync<GuardRefusedException>(() => session.SaveChangesAsync());
        ex.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(GuardState);
    }

    [Fact]
    public async Task a_guard_queued_from_a_session_listener_surfaces_as_the_applications_exception()
    {
        await UseGuardAsync();

        await using var session = theStore.LightweightSession(new SessionOptions
        {
            Listeners = { new GuardingListener(Guard) }
        });
        session.Store(Target.Random());

        await Should.ThrowAsync<GuardRefusedException>(() => session.SaveChangesAsync());
    }

    [Fact]
    public async Task a_raw_sql_query_surfaces_as_the_applications_exception()
    {
        await UseGuardAsync();

        await using var query = theStore.QuerySession();

        await Should.ThrowAsync<GuardRefusedException>(() => query.QueryAsync<int>($"select {Guard}"));
    }

    [Fact]
    public async Task a_linq_query_surfaces_as_the_applications_exception()
    {
        await UseGuardAsync();

        // A row, or PostgreSQL never evaluates the WHERE and the guard never runs
        await using (var session = theStore.LightweightSession())
        {
            session.Store(Target.Random());
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();

        await Should.ThrowAsync<GuardRefusedException>(() =>
            query.Query<Target>().Where(x => x.MatchesSql($"{Guard} = 1")).ToListAsync());
    }

    [Fact]
    public async Task an_error_the_application_does_not_claim_still_gets_marten_s_translation()
    {
        await UseGuardAsync();

        await using var query = theStore.QuerySession();

        var ex = await Should.ThrowAsync<MartenCommandException>(() =>
            query.QueryAsync<int>("select no_such_column from pg_class"));
        ex.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UndefinedColumn);
    }

    [Fact]
    public async Task without_a_registration_the_guard_is_a_marten_command_exception_as_before()
    {
        // Transforms belong to the store that registered them, never to the process
        await UseGuardAsync(registerTransform: false);

        await using var session = theStore.LightweightSession();
        session.QueueSqlCommand($"select {Guard}");

        var ex = await Should.ThrowAsync<MartenCommandException>(() => session.SaveChangesAsync());
        ex.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(GuardState);
    }

    private class GuardingListener(string guard): DocumentSessionListenerBase
    {
        public override Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
        {
            session.QueueSqlCommand($"select {guard}");
            return Task.CompletedTask;
        }
    }
}
