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
using Weasel.Postgresql;
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
    public async Task keeps_each_databases_results_together()
    {
        var users = await theStore.Advanced.QueryAcrossAllDatabasesAsync(
            async (session, ct) => await session.Query<User>().ToListAsync(ct), 3);

        // The user names are "<tenant>-<n>": once a tenant's run ends, it must not come back
        var tenants = users.Select(x => x.UserName[..x.UserName.LastIndexOf('-')]).ToList();
        var runs = tenants.Where((tenant, i) => i == 0 || tenants[i - 1] != tenant).ToList();
        runs.ShouldBe(runs.Distinct().ToList());
        runs.Count.ShouldBe(3);
    }

    [Fact]
    public async Task hands_each_database_to_the_query_once()
    {
        var seen = await theStore.Advanced.QueryAcrossAllDatabasesAsync<string>(
            (session, database, ct) => Task.FromResult<IReadOnlyList<string>>([database.Identifier]));

        var all = await theStore.Storage.AllDatabases();
        seen.ShouldBe(all.Select(x => x.Identifier), ignoreOrder: true);
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

/// <summary>
///     Under sharded tenancy every document is conjoined, and each session that
///     <see cref="AdvancedOperations.QueryAcrossAllDatabasesAsync{T}(System.Func{IQuerySession,CancellationToken,Task{IReadOnlyList{T}}},int,CancellationToken)" />
///     opens is its database's default tenant. Pins down that a plain query finds nothing and <c>AnyTenant()</c>
///     finds every tenant's rows on every shard.
/// </summary>
[Collection("sharded-tenancy")]
public class query_across_all_sharded_databases: IAsyncLifetime
{
    private readonly ShardedTenancyFixture _fixture;
    private IDocumentStore _store = null!;

    public query_across_all_sharded_databases(ShardedTenancyFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            try { await conn.DropSchemaAsync("sharded"); } catch { }
        }

        foreach (var connStr in _fixture.ConnectionStrings.Values)
        {
            await using var tenantConn = new NpgsqlConnection(connStr);
            await tenantConn.OpenAsync();
            try { await tenantConn.DropSchemaAsync("tenants"); } catch { }
            await ShardedTenancyFixture.cleanMartenObjectsInPublicSchema(tenantConn);
        }

        _store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = "sharded";
                x.PartitionSchemaName = "tenants";

                foreach (var (dbName, connStr) in _fixture.ConnectionStrings)
                {
                    x.AddDatabase(dbName, connStr);
                }
            });

            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
            opts.RegisterDocumentType<User>();
        });

        foreach (var db in (await _store.Storage.AllDatabases()))
        {
            await db.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        // Two tenants on the first shard, one on each of the others
        var tenants = new[]
        {
            ("q_alpha", _fixture.DbNames[0]),
            ("q_beta", _fixture.DbNames[0]),
            ("q_gamma", _fixture.DbNames[1]),
            ("q_delta", _fixture.DbNames[2])
        };

        foreach (var (tenantId, databaseId) in tenants)
        {
            await _store.Advanced.AddTenantToShardAsync(tenantId, databaseId, CancellationToken.None);

            await using var session = _store.LightweightSession(tenantId);
            session.Store(new User { UserName = tenantId });
            await session.SaveChangesAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    [Fact]
    public async Task a_plain_query_only_sees_the_default_tenant_and_finds_nothing()
    {
        var users = await _store.Advanced.QueryAcrossAllDatabasesAsync(
            async (session, ct) => await session.Query<User>().ToListAsync(ct));

        users.ShouldBeEmpty();
    }

    [Fact]
    public async Task any_tenant_finds_every_tenants_rows_on_every_shard()
    {
        var users = await _store.Advanced.QueryAcrossAllDatabasesAsync(
            async (session, ct) => await session.Query<User>().Where(x => x.AnyTenant()).ToListAsync(ct));

        users.Select(x => x.UserName).OrderBy(x => x)
            .ShouldBe(["q_alpha", "q_beta", "q_delta", "q_gamma"]);
    }
}
