using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

public class MartenOrderTally
{
    public string Id { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// A perfectly ordinary Marten-stored multi-stream projection over the same events, registered
/// alongside the EF Core one. It applies events by overriding <c>DetermineActionAsync</c> rather
/// than by Create/Apply conventions only because this test project does not wire the projection
/// source generator; what matters here is where its data lives, not how it is built.
/// </summary>
public class MartenOrderTallyProjection: MultiStreamProjection<MartenOrderTally, string>
{
    public MartenOrderTallyProjection()
    {
        Identity<CustomerOrderPlaced>(e => e.CustomerName);
    }

    public override ValueTask<(MartenOrderTally?, ActionType)> DetermineActionAsync(
        IQuerySession session,
        MartenOrderTally? snapshot,
        string identity,
        IIdentitySetter<MartenOrderTally, string> identitySetter,
        IReadOnlyList<IEvent> events,
        CancellationToken cancellation)
    {
        snapshot ??= new MartenOrderTally { Id = identity };
        snapshot.Count += events.Count;

        return new ValueTask<(MartenOrderTally?, ActionType)>((snapshot, ActionType.Store));
    }
}

/// <summary>
/// #5329: an EF Core projection's data lives in a DbContext-managed table, never in Marten's
/// conventional <c>mt_doc_&lt;tdoc&gt;</c> document table -- <c>FetchProjectionStorageAsync</c>
/// already skips <c>EnsureStorageExistsAsync(typeof(TDoc))</c> for these types, so that table is
/// never created. Rebuild teardown asked the wrong question anyway: every aggregate projection
/// inherits <c>Options.DeleteViewTypeOnTeardown&lt;TDoc&gt;()</c> from JasperFx, so the rebuild
/// queued a <c>truncate table mt_doc_customerorderhistory</c> that
///
/// <list type="bullet">
/// <item>blew up with <c>42P01: relation does not exist</c> under
/// <see cref="AutoCreate.None"/> -- the reported symptom; and</item>
/// <item>silently left the REAL EF Core table fully populated even when it did not blow up, so a
/// rebuild replayed on top of stale rows -- the quieter and worse half of the same bug.</item>
/// </list>
///
/// Every test below runs with <see cref="AutoCreate.None"/> against a schema built by a separate
/// migration pass, which is the reporter's deployment shape and the only one where the first
/// failure is visible rather than papered over by Marten auto-creating an unused table. The store
/// also carries a conventional Marten projection throughout, so the third test can prove the fix
/// did not go too far the other way.
/// </summary>
public class Bug_5329_efcore_projection_rebuild_teardown: IAsyncLifetime
{
    private const string SchemaName = "efcore_rebuild_5329";

    public async ValueTask InitializeAsync()
    {
        await dropSchemaAsync();

        // Stand up the schema the way the reporter does: a migration pass that knows about the
        // EF Core entity tables and the event store, but does not register the EF Core projection.
        using var schemaStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AddEventType<CustomerOrderPlaced>();
            opts.Events.AddEventType<CustomerOrderCompleted>();
            opts.AddEntityTablesFromDbContext<TestDbContext>();

            // A conventional, Marten-stored projection shares the store, so every rebuild below
            // exercises the new teardown guard in a mixed store rather than an EF-only one.
            opts.Projections.Add(new MartenOrderTallyProjection(), ProjectionLifecycle.Async);
        });

        await schemaStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // The premise of the whole bug: the EF Core table exists, the Marten document table for the
        // EF-backed aggregate does not -- while the conventional projection's document table does.
        (await tableExistsAsync("ef_customer_order_histories")).ShouldBeTrue();
        (await tableExistsAsync("mt_doc_customerorderhistory")).ShouldBeFalse();
        (await tableExistsAsync("mt_doc_martenordertally")).ShouldBeTrue();
    }

    public async ValueTask DisposeAsync()
    {
        await dropSchemaAsync();
    }

    private static DocumentStore buildProjectionStore()
    {
        return DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Add(new CustomerOrderHistoryProjection(), ProjectionLifecycle.Async);
            opts.Projections.Add(new MartenOrderTallyProjection(), ProjectionLifecycle.Async);
        });
    }

    [Fact]
    public async Task rebuild_does_not_try_to_truncate_a_marten_document_table()
    {
        using var store = buildProjectionStore();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(Guid.NewGuid().ToString(),
                new CustomerOrderPlaced(Guid.NewGuid(), "Rebuild customer", 100.00m));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var daemon = await store.BuildProjectionDaemonAsync();

        // Before the fix this threw Npgsql.PostgresException 42P01:
        // relation "efcore_rebuild_5329.mt_doc_customerorderhistory" does not exist
        await daemon.RebuildProjectionAsync<CustomerOrderHistoryProjection>(
            TestContext.Current.CancellationToken);

        var (totalOrders, totalSpent) = await readHistoryAsync("Rebuild customer");
        totalOrders.ShouldBe(1);
        totalSpent.ShouldBe(100.00m);

        // ...and the rebuild must not have conjured the Marten table into existence either.
        (await tableExistsAsync("mt_doc_customerorderhistory")).ShouldBeFalse();
    }

    [Fact]
    public async Task rebuild_clears_stale_rows_out_of_the_ef_core_table()
    {
        using var store = buildProjectionStore();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(Guid.NewGuid().ToString(),
                new CustomerOrderPlaced(Guid.NewGuid(), "Kept customer", 25.00m));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // A row no event in the store can produce. A correct teardown wipes it; the old behavior
        // truncated an unrelated Marten table and left this sitting there forever.
        await executeAsync(
            "insert into ef_customer_order_histories (id, total_orders, total_spent) values ('Ghost customer', 99, 999.99)");

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<CustomerOrderHistoryProjection>(
            TestContext.Current.CancellationToken);

        (await rowExistsAsync("Ghost customer")).ShouldBeFalse();

        var (totalOrders, totalSpent) = await readHistoryAsync("Kept customer");
        totalOrders.ShouldBe(1);
        totalSpent.ShouldBe(25.00m);
    }

    /// <summary>
    /// The guard against fixing this in the wrong direction. Skipping teardown too eagerly would
    /// leave a rebuilt Marten projection sitting on stale rows and still report success -- a worse
    /// bug than the one being fixed, because nothing throws. This rebuilds an ordinary Marten
    /// projection out of the SAME store as the EF Core one, so the guard is live, and proves its
    /// document table is still truncated.
    /// </summary>
    [Fact]
    public async Task rebuild_of_a_conventional_marten_projection_still_truncates_its_document_table()
    {
        using var store = buildProjectionStore();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(Guid.NewGuid().ToString(),
                new CustomerOrderPlaced(Guid.NewGuid(), "Kept customer", 25.00m));
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // A Marten document no event in the store can produce. Only a real truncate removes it.
        await using (var session = store.LightweightSession())
        {
            session.Store(new MartenOrderTally { Id = "Ghost customer", Count = 99 });
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var daemon = await store.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<MartenOrderTallyProjection>(
            TestContext.Current.CancellationToken);

        await using var query = store.QuerySession();
        var tallies = await query.Query<MartenOrderTally>()
            .ToListAsync(TestContext.Current.CancellationToken);

        tallies.Select(x => x.Id).ShouldBe(["Kept customer"]);
        tallies.Single().Count.ShouldBe(1);
    }

    private static async Task<NpgsqlConnection> openConnectionAsync()
    {
        var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var setSchema = conn.CreateCommand();
        setSchema.CommandText = $"SET search_path TO {SchemaName}";
        await setSchema.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return conn;
    }

    private static async Task executeAsync(string sql)
    {
        await using var conn = await openConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<bool> tableExistsAsync(string tableName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select to_regclass(@name) is not null";
        cmd.Parameters.AddWithValue("name", $"{SchemaName}.{tableName}");
        return (bool)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<bool> rowExistsAsync(string id)
    {
        await using var conn = await openConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select exists(select 1 from ef_customer_order_histories where id = @id)";
        cmd.Parameters.AddWithValue("id", id);
        return (bool)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<(int TotalOrders, decimal TotalSpent)> readHistoryAsync(string id)
    {
        await using var conn = await openConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select total_orders, total_spent from ef_customer_order_histories where id = @id";
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
        return (reader.GetInt32(0), reader.GetDecimal(1));
    }

    private static async Task dropSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP SCHEMA IF EXISTS {SchemaName} CASCADE";
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
