using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace MultiTenancyTests;

[Collection("multi-tenancy")]
public class query_across_all_databases: IAsyncLifetime
{
    private IDocumentStore theStore;

    public async ValueTask InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var cs1 = await TenantDatabases.CreateIfNotExistsAsync(conn, "query_all_1");
        var cs2 = await TenantDatabases.CreateIfNotExistsAsync(conn, "query_all_2");
        var cs3 = await TenantDatabases.CreateIfNotExistsAsync(conn, "query_all_3");

        theStore = DocumentStore.For(opts =>
        {
            opts.MultiTenantedDatabases(x =>
            {
                x.AddSingleTenantDatabase(cs1, "query_all_1");
                x.AddSingleTenantDatabase(cs2, "query_all_2");
                x.AddSingleTenantDatabase(cs3, "query_all_3");
            });
            opts.RegisterDocumentType<User>();
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();

        var tenantNumber = 0;
        foreach (var tenant in new[] { "query_all_1", "query_all_2", "query_all_3" })
        {
            tenantNumber++;
            await using var session = theStore.LightweightSession(tenant);
            for (var i = 0; i < tenantNumber; i++)
            {
                session.Store(new User { UserName = $"{tenant}-{i}" });
            }

            await session.SaveChangesAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        theStore?.Dispose();
        return default;
    }

    [Fact]
    public async Task flattens_the_results_of_every_database()
    {
        var users = await theStore.Advanced.QueryAcrossAllDatabasesAsync(
            async (session, ct) => await session.Query<User>().ToListAsync(ct), 2);

        users.Count.ShouldBe(6);
        users.Select(x => x.UserName).Distinct().Count().ShouldBe(6);
    }

    [Fact]
    public async Task hands_the_database_to_the_query_and_keeps_database_order()
    {
        var seen = await theStore.Advanced.QueryAcrossAllDatabasesAsync<string>(
            (session, database, ct) => Task.FromResult<IReadOnlyList<string>>([database.Identifier]));

        var all = await theStore.Storage.AllDatabases();
        seen.ShouldBe(all.Select(x => x.Identifier).ToList());
        seen.Count.ShouldBe(3);
    }

    [Fact]
    public async Task never_runs_more_queries_at_once_than_the_limit()
    {
        var running = 0;
        var peak = 0;

        await theStore.Advanced.QueryAcrossAllDatabasesAsync<int>(async (_, ct) =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            await Task.Delay(100, ct);
            Interlocked.Decrement(ref running);
            return [];
        }, 2);

        peak.ShouldBe(2);
    }

    [Fact]
    public async Task a_failing_database_fails_the_whole_query()
    {
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await theStore.Advanced.QueryAcrossAllDatabasesAsync<int>(
                (_, _) => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task rejects_a_parallelism_below_one()
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await theStore.Advanced.QueryAcrossAllDatabasesAsync<int>(
                (_, _) => Task.FromResult<IReadOnlyList<int>>([]), 0));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current) break;
        }
    }
}
