using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

/// <summary>
/// #5603: when a transient failure hits the <c>COMMIT</c> itself, Marten's write resilience pipeline
/// runs the whole transaction again — and the retry commits the events WITHOUT the inline EF Core
/// projection's row. <c>SaveChangesAsync</c> returns successfully, so the caller never learns that
/// the read model has diverged from the stream.
///
/// <para>
/// <c>DbContextTransactionParticipant.BeforeCommitAsync</c> flushes with
/// <c>DbContext.SaveChangesAsync(token)</c>, which is <c>SaveChangesAsync(acceptAllChangesOnSuccess:
/// true)</c>. EF Core marks every tracked entry <c>Unchanged</c> the moment its statements execute —
/// before the enclosing transaction commits. The participant call sits inside the block the pipeline
/// retries (<c>AutoClosingLifetime.ExecuteBatchPagesAsync</c>, just ahead of <c>CommitAsync</c>), so
/// the second attempt re-runs <c>BeforeCommitAsync</c> against a change tracker that believes it has
/// nothing left to save. Marten's own operations are replayed; the projection's are not.
/// </para>
///
/// <para>
/// The failure is silent and permanent until the projection is rebuilt. A new aggregate's row is
/// simply missing while its stream exists — and a projection whose <c>ApplyEvent</c> needs the
/// existing row then fails on every later event for that stream. An update to an existing row is
/// lost with the read model left holding its previous state and no error anywhere.
/// </para>
///
/// <para>
/// A deferred constraint trigger supplies the transient failure. It raises <c>40P01</c>
/// (deadlock_detected, which Marten's classifier treats as retryable) from <c>AFTER INSERT ...
/// DEFERRABLE INITIALLY DEFERRED</c> on <c>mt_events</c>, so it fires at COMMIT rather than at the
/// insert — which is the whole point. A counter sequence makes it fail the first commit only, so the
/// retry is the attempt under test. Connection loss, failover and real deferred constraints reach
/// the same place in production.
/// </para>
/// </summary>
public class Bug_5603_inline_projection_row_lost_when_commit_is_retried: IAsyncLifetime
{
    private const string SchemaName = "efcore_retry_5603";

    private DocumentStore theStore = null!;

    public async ValueTask InitializeAsync()
    {
        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.Add(new OrderAggregate(), ProjectionLifecycle.Inline);
        });

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public ValueTask DisposeAsync()
    {
        theStore?.Dispose();
        return default;
    }

    private static async Task executeAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<long> scalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn);
        var raw = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return raw is long l ? l : Convert.ToInt64(raw ?? 0L);
    }

    private static Task failTheNextCommitOnceAsync()
    {
        // Idempotent: Clean.CompletelyRemoveAllAsync only knows about Marten's own objects, so the
        // trigger and counter from a sibling test in this class outlive it.
        return executeAsync($@"
drop trigger if exists fail_first_commit on {SchemaName}.mt_events;
drop sequence if exists {SchemaName}.commit_attempts;

create sequence {SchemaName}.commit_attempts;

create or replace function {SchemaName}.fail_first_commit() returns trigger language plpgsql as $$
begin
    if nextval('{SchemaName}.commit_attempts') = 1 then
        raise exception 'transient failure at commit' using errcode = '40P01';
    end if;
    return new;
end $$;

create constraint trigger fail_first_commit after insert on {SchemaName}.mt_events
    deferrable initially deferred for each row
    execute function {SchemaName}.fail_first_commit();
");
    }

    [Fact]
    public async Task the_retried_transaction_commits_the_projection_row_too()
    {
        await failTheNextCommitOnceAsync();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(id, new OrderPlaced(id, "retried customer", 42m, 3));

            // Succeeds: the first COMMIT fails transiently and the pipeline runs the whole
            // transaction again, which does commit.
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // The trigger really did see two commits, so the retry under test actually happened. Without
        // this the test could pass for the uninteresting reason that nothing was ever retried.
        (await scalarAsync($"select last_value from {SchemaName}.commit_attempts")).ShouldBe(2);

        // The events are there...
        await using var query = theStore.QuerySession();
        var state = await query.Events.FetchStreamStateAsync(id, TestContext.Current.CancellationToken);
        state.ShouldNotBeNull();
        state.Version.ShouldBe(1);

        // ...and so must the inline projection's row be. On the unfixed code this is 0: the retry
        // found an already-accepted change tracker and wrote nothing.
        (await scalarAsync($"select count(*) from {SchemaName}.ef_orders where id = '{id}'")).ShouldBe(1);

        // The projection also writes a side-effect entity straight through the DbContext, which is
        // lost the same way and has no stream of its own to reveal the loss.
        (await scalarAsync($"select count(*) from {SchemaName}.ef_order_summaries where id = '{id}'"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task a_retry_does_not_lose_an_update_to_an_existing_row()
    {
        // The nastier half of the report: the row already exists, so nothing downstream ever errors.
        // The read model simply keeps its previous state forever.
        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(id, new OrderPlaced(id, "steady customer", 42m, 3));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await failTheNextCommitOnceAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(id, new OrderShipped(id));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await scalarAsync($"select last_value from {SchemaName}.commit_attempts")).ShouldBe(2);

        (await scalarAsync(
                $"select count(*) from {SchemaName}.ef_orders where id = '{id}' and is_shipped = true"))
            .ShouldBe(1);
    }
}
