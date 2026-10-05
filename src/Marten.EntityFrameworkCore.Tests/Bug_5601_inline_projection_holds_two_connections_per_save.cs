using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

/// <summary>
/// #5601: an inline <c>EfCoreSingleStreamProjection</c> makes every <c>SaveChangesAsync</c> hold TWO
/// pooled connections at once. <c>EfCoreDbContextFactory.Create</c> opens a placeholder connection
/// when the projection's storage is built — it has to, because the storage's <c>LoadAsync</c> reads
/// the existing row before the commit — and that placeholder is only handed back in
/// <c>DbContextTransactionParticipant.BeforeCommitAsync</c>, which runs AFTER
/// <c>AutoClosingLifetime.ExecuteBatchPagesAsync</c> has acquired the session's own commit
/// connection.
///
/// <para>
/// The two are therefore held in the wrong order to ever queue cleanly. Once the number of
/// concurrent saves reaches <c>Maximum Pool Size</c>, every connection in the pool is a placeholder,
/// every one of those saves is waiting for a commit connection that only another save could release,
/// and nothing completes until the pool <c>Timeout</c> fires. It is a true deadlock, not contention:
/// waiting longer never helps, and at pool size 1 a single save can never succeed at all.
/// </para>
///
/// <para>
/// Reported from a load test of an HTTP service — 128 concurrent requests against a pool of 100,
/// p99 above 21 seconds, with a dump showing 20 threads parked inside the placeholder's
/// <c>Open()</c>. The pool size here is tiny only to make the same ratio deterministic and fast; the
/// bug is about holding two connections per save, so the ratio is what matters, not the absolute
/// numbers.
/// </para>
///
/// <para>
/// Both tests pin the behaviour against a control store with no projection registered, so a failure
/// can never be read as "the pool is simply too small" — the same saves through the same pool must
/// succeed without the projection.
/// </para>
/// </summary>
public class Bug_5601_inline_projection_holds_two_connections_per_save: IAsyncLifetime
{
    private const string SchemaName = "efcore_pool_5601";

    public async ValueTask InitializeAsync()
    {
        // Build the schema through an UNCONSTRAINED pool. The constrained stores below must exercise
        // only the save path -- schema creation legitimately wants several connections, and letting
        // it run inside the one-connection store would prove nothing about this bug.
        await using var setup = storeFor(ConnectionSource.ConnectionString, withProjection: true);
        await setup.Advanced.Clean.CompletelyRemoveAllAsync();
        await setup.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public ValueTask DisposeAsync() => default;

    private static DocumentStore storeFor(string connectionString, bool withProjection)
    {
        return DocumentStore.For(opts =>
        {
            opts.Connection(connectionString);
            opts.DatabaseSchemaName = SchemaName;
            if (withProjection)
            {
                opts.Add(new OrderAggregate(), ProjectionLifecycle.Inline);
            }
        });
    }

    private static string pooled(int maxPoolSize, int timeoutSeconds)
    {
        return new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            MaxPoolSize = maxPoolSize,
            Timeout = timeoutSeconds,
            ApplicationName = SchemaName
        }.ConnectionString;
    }

    private static async Task saveOneAsync(IDocumentStore store)
    {
        await using var session = store.LightweightSession();
        var id = Guid.NewGuid();
        session.Events.StartStream(id, new OrderPlaced(id, "pool probe", 10m, 1));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task a_single_save_completes_through_a_pool_of_one()
    {
        var connectionString = pooled(maxPoolSize: 1, timeoutSeconds: 5);

        // Control: one connection is genuinely enough for a save, so a failure below is about the
        // projection holding a second one and not about the pool being too small to save at all.
        await using (var withoutProjection = storeFor(connectionString, withProjection: false))
        {
            await saveOneAsync(withoutProjection);
        }

        await using var withProjection = storeFor(connectionString, withProjection: true);
        await saveOneAsync(withProjection);
    }

    [Fact]
    public async Task concurrent_saves_all_complete_through_a_pool_smaller_than_the_concurrency()
    {
        const int concurrency = 16;
        var connectionString = pooled(maxPoolSize: 4, timeoutSeconds: 10);

        await using (var withoutProjection = storeFor(connectionString, withProjection: false))
        {
            (await countSavedAsync(withoutProjection, concurrency)).ShouldBe(concurrency);
        }

        // Same pool, same concurrency, one inline EF Core projection added. Every save needs two
        // connections, so all four slots fill with placeholders and no save can reach its commit.
        await using var withProjection = storeFor(connectionString, withProjection: true);
        (await countSavedAsync(withProjection, concurrency)).ShouldBe(concurrency);
    }


    private static async Task<int> countSavedAsync(IDocumentStore store, int concurrency)
    {
        var results = await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            try
            {
                await saveOneAsync(store);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        })));

        return results.Count(x => x);
    }
}
