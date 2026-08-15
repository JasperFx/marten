using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

public record LeakProbePlaced(Guid OrderId, string CustomerName, decimal Amount);

/// <summary>An event no projection here is interested in.</summary>
public record UnrelatedThing(string What);

public class LeakProbeProjection
    : EfCoreMultiStreamProjection<CustomerOrderHistory, string, TestDbContext>
{
    public LeakProbeProjection()
    {
        Identity<LeakProbePlaced>(e => e.CustomerName);
    }

    public override CustomerOrderHistory? ApplyEvent(CustomerOrderHistory? snapshot,
        string identity, IEvent @event, TestDbContext dbContext)
    {
        if (@event.Data is LeakProbePlaced placed && placed.CustomerName == "explode")
        {
            throw new InvalidOperationException("boom from inside the projection");
        }

        snapshot ??= new CustomerOrderHistory { Id = identity };
        snapshot.TotalOrders++;
        return snapshot;
    }
}

/// <summary>
/// #5228: the EF Core integration's placeholder NpgsqlConnection was released only at the end of
/// <c>DbContextTransactionParticipant.BeforeCommitAsync</c>, i.e. only on the success path. Any
/// route that never reached that line stranded a pooled connection:
///
/// <list type="bullet">
/// <item>a projection that throws while applying;</item>
/// <item>an optimistic concurrency failure on <c>SaveChangesAsync</c> — and note that an inline
/// multi-stream projection has its storage built for events it will not even process, because
/// the grouper's empty result is only known after the connection is already open;</item>
/// <item>a throw from inside <c>BeforeCommitAsync</c> itself.</item>
/// </list>
///
/// Each test drives one failing save many times and asserts the pool did not grow. Counting
/// connections rather than inspecting internals is the point: the leak was only ever observable
/// as pool exhaustion in production.
/// </summary>
public class Bug_5228_inline_projection_connection_leak: IAsyncLifetime
{
    private const string SchemaName = "efcore_leak_5228";

    // Deliberately NOT small enough to exhaust. An earlier version of this test used a tiny pool
    // and asserted that a connection could still be acquired -- which does reproduce the bug, but
    // the unfixed failure mode is a multi-minute block on the pool rather than an assertion, and a
    // hang is a miserable CI failure. Counting backend connections instead fails fast with a
    // number that says what went wrong.
    // Bounded well under the server's max_connections (100 by default) because this class runs in
    // parallel with the rest of the suite. Steady state with the fix is 2; without it the loops
    // below would otherwise happily eat every backend the server has and take unrelated tests
    // down with them.
    private const int MaxPoolSize = 30;
    private const int Iterations = 20;

    private DocumentStore theStore = null!;
    private string theConnectionString = null!;
    private string theObserverConnectionString = null!;

    public async ValueTask InitializeAsync()
    {
        theConnectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            MaxPoolSize = MaxPoolSize,
            ApplicationName = SchemaName,
            Timeout = 5
        }.ConnectionString;

        // A separate identity so the observer below never counts itself.
        theObserverConnectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            ApplicationName = SchemaName + "_observer"
        }.ConnectionString;

        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(theConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Add(new LeakProbeProjection(), ProjectionLifecycle.Inline);
        });

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

        // Force the schema into existence once so the loops below only exercise the save path.
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(Guid.NewGuid().ToString(), new LeakProbePlaced(Guid.NewGuid(), "warmup", 1m));
        await session.SaveChangesAsync();
    }

    public ValueTask DisposeAsync()
    {
        theStore?.Dispose();
        return default;
    }

    [Fact]
    public async Task a_throwing_projection_does_not_strand_the_placeholder_connection()
    {
        for (var i = 0; i < Iterations; i++)
        {
            await using var session = theStore.LightweightSession();
            session.Events.StartStream(Guid.NewGuid().ToString(),
                new LeakProbePlaced(Guid.NewGuid(), "explode", 1m));

            await Should.ThrowAsync<Exception>(async () =>
                await session.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await assertNoConnectionsWereStrandedAsync();
    }

    [Fact]
    public async Task an_optimistic_concurrency_failure_does_not_strand_the_placeholder_connection()
    {
        // The reporter's second case, and the nastier one: these events are not the projection's
        // at all. The grouper returns nothing so the projector never runs -- but the placeholder
        // connection was already opened when the projection's storage was built.
        for (var i = 0; i < Iterations; i++)
        {
            var streamKey = Guid.NewGuid().ToString();

            await using (var seed = theStore.LightweightSession())
            {
                seed.Events.StartStream(streamKey, new UnrelatedThing("first"));
                await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await using var session = theStore.LightweightSession();

            // Append at a version that is already taken -> optimistic concurrency failure on save
            session.Events.Append(streamKey, 1, new UnrelatedThing("collides"));

            await Should.ThrowAsync<Exception>(async () =>
                await session.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        await assertNoConnectionsWereStrandedAsync();
    }

    [Fact]
    public async Task the_success_path_still_releases_exactly_once()
    {
        // Guard against the fix double-disposing, and against it releasing so late that a
        // long-lived workload accumulates connections it has finished with.
        for (var i = 0; i < Iterations; i++)
        {
            await using var session = theStore.LightweightSession();
            session.Events.StartStream(Guid.NewGuid().ToString(),
                new LeakProbePlaced(Guid.NewGuid(), $"customer-{i}", 1m));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await assertNoConnectionsWereStrandedAsync();
    }

    /// <summary>
    /// A leaked placeholder is checked out of the pool forever and stays open on the server, so it
    /// shows up in pg_stat_activity and never leaves. One leak per iteration means the backend
    /// count tracks Iterations; the fix keeps it at the handful the pool actually reuses.
    /// </summary>
    private async Task assertNoConnectionsWereStrandedAsync()
    {
        await using var observer = new NpgsqlConnection(theObserverConnectionString);
        await observer.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = observer.CreateCommand();
        cmd.CommandText = "select count(*) from pg_stat_activity where application_name = @name";
        cmd.Parameters.AddWithValue("name", SchemaName);

        var open = (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;

        // Measured: 2 with the fix, against 21 / 22 / 41 for the three cases without it. The bound
        // is deliberately loose -- the property under test is that the count does not scale with
        // the number of saves, not that it hits an exact number.
        open.ShouldBeLessThan(Iterations,
            $"{open} backend connections are still open after {Iterations} saves -- the EF Core placeholder connection is being stranded");
    }
}
