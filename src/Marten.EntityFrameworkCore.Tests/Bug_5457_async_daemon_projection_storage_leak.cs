using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

public record DaemonLeakProbePlaced(Guid OrderId, string CustomerName, decimal Amount);

public class DaemonLeakProbeProjection: EfCoreSingleStreamProjection<Order, Guid, TestDbContext>
{
    public override Order ApplyEvent(Order? snapshot, Guid identity, IEvent @event,
        TestDbContext dbContext, IQuerySession session)
    {
        // The load this forces is the query the reporter saw stranded on every leaked backend:
        // "SELECT ... FROM orders AS o WHERE o.id = $1 LIMIT 1".
        snapshot ??= new Order { Id = identity };

        if (@event.Data is DaemonLeakProbePlaced placed)
        {
            snapshot.CustomerName = placed.CustomerName;
            snapshot.TotalAmount = placed.Amount;
            snapshot.ItemCount = 1;
        }

        return snapshot;
    }
}

/// <summary>
/// #5457. An <see cref="EfCoreSingleStreamProjection{TDoc,TId,TDbContext}"/> under
/// <see cref="ProjectionLifecycle.Async"/> was reported to leak one PostgreSQL backend per daemon
/// batch, monotonically, until the server refused new connections. Every leaked backend's last
/// statement was the projection's own aggregate load.
///
/// <para>
/// The counting strategy is lifted from <see cref="Bug_5228_inline_projection_connection_leak"/>:
/// tag the store's connection string with a distinct <c>ApplicationName</c> and count matching rows
/// in <c>pg_stat_activity</c>, because the leak is only ever observable as pool exhaustion. The
/// difference here is that a running daemon has a real steady-state footprint of its own, so the
/// assertion is on GROWTH across batches rather than on an absolute count -- a per-batch leak scales
/// with the number of batches, daemon overhead does not.
/// </para>
/// </summary>
[Collection("OneOffs")]
public class Bug_5457_async_daemon_projection_storage_leak: IAsyncLifetime
{
    private const string SchemaName = "efcore_leak_5457";

    // Generous enough that a real leak shows up as a count rather than as a multi-minute block on
    // the pool -- a hang is a miserable CI failure, and the number is what says what went wrong.
    private const int MaxPoolSize = 60;
    private const int Batches = 15;

    private DocumentStore theStore = null!;
    private string theObserverConnectionString = null!;

    public async ValueTask InitializeAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            MaxPoolSize = MaxPoolSize,
            ApplicationName = SchemaName,
            Timeout = 5
        }.ConnectionString;

        theObserverConnectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            ApplicationName = SchemaName + "_observer"
        }.ConnectionString;

        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(connectionString);

            // The schema name matters: EfCoreDbContextFactory.Create only opens its connection
            // eagerly when one is configured, and an unopened connection has no backend to leak.
            opts.DatabaseSchemaName = SchemaName;
            opts.Add(new DaemonLeakProbeProjection(), ProjectionLifecycle.Async);
        });

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public ValueTask DisposeAsync()
    {
        theStore?.Dispose();
        return default;
    }

    [Fact]
    public async Task the_daemon_does_not_strand_a_backend_per_batch()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        // One batch first, so the reading below is taken against a daemon that has already paid
        // its one-off costs -- schema checks, the high water detector's connection, and so on.
        await AppendOneStreamAsync();
        await theStore.WaitForNonStaleProjectionDataAsync(30.Seconds());

        var before = await CountBackendsAsync();

        for (var i = 0; i < Batches; i++)
        {
            await AppendOneStreamAsync();

            // Forces the daemon to actually drain, so each iteration is at least one real batch
            // rather than several appends coalescing into one.
            await theStore.WaitForNonStaleProjectionDataAsync(30.Seconds());
        }

        var after = await CountBackendsAsync();

        // Guard against a vacuously green leak test: if the projection never actually ran, no EF
        // Core DbContext was ever built and there was nothing that COULD have leaked. This is
        // asserted BEFORE the connection count so a broken harness reports as a broken harness.
        var projected = await CountProjectedRowsAsync();
        projected.ShouldBe(Batches + 1, "the projection did not run, so this test proves nothing");

        // One leak per batch makes `after` climb by at least Batches. The bound is deliberately
        // loose: the property under test is that the count does not scale with the number of
        // batches, not that it lands on an exact number.
        (after - before).ShouldBeLessThan(Batches,
            $"backend connections grew from {before} to {after} across {Batches} daemon batches -- "
            + "the EF Core projection storage's DbContext connection is being stranded per batch");
    }

    private async Task AppendOneStreamAsync()
    {
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(Guid.NewGuid(),
            new DaemonLeakProbePlaced(Guid.NewGuid(), "customer", 10m));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> CountProjectedRowsAsync()
    {
        await using var observer = new NpgsqlConnection(theObserverConnectionString);
        await observer.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = observer.CreateCommand();
        cmd.CommandText = $"select count(*) from {SchemaName}.ef_orders";

        return (int)(long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task<int> CountBackendsAsync()
    {
        await using var observer = new NpgsqlConnection(theObserverConnectionString);
        await observer.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = observer.CreateCommand();
        cmd.CommandText = "select count(*) from pg_stat_activity where application_name = @name";
        cmd.Parameters.AddWithValue("name", SchemaName);

        return (int)(long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
