using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Linq;
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
    public async Task tenant_ids_visit_only_the_databases_of_those_tenants()
    {
        var seen = await theStore.Advanced.QueryAcrossAllDatabasesAsync<string>(
            (session, database, ct) => Task.FromResult<IReadOnlyList<string>>([database.Identifier]),
            8, CancellationToken.None, "query_all_2");

        seen.ShouldBe(["query_all_2"]);

        var users = await theStore.Advanced.QueryAcrossAllDatabasesAsync(
            async (session, ct) => await session.Query<User>().ToListAsync(ct),
            8, CancellationToken.None, "query_all_2", "query_all_3");

        users.Select(x => x.UserName).OrderBy(x => x)
            .ShouldBe(["query_all_2-0", "query_all_2-1", "query_all_3-0", "query_all_3-1", "query_all_3-2"]);
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
///     Several tenants per database, every document conjoined. A query across all databases reads every
///     tenant; the tenant id overload visits only the databases that hold those tenants.
/// </summary>
[Collection("multi-tenancy")]
public class query_across_all_conjoined_databases: IAsyncLifetime
{
    private IDocumentStore theStore;

    public async ValueTask InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var cs1 = await TenantDatabases.CreateIfNotExistsAsync(conn, "query_conj_1");
        var cs2 = await TenantDatabases.CreateIfNotExistsAsync(conn, "query_conj_2");

        theStore = DocumentStore.For(opts =>
        {
            opts.MultiTenantedDatabases(x =>
            {
                x.AddMultipleTenantDatabase(cs1, "query_conj_1").ForTenants("red", "blue");
                x.AddMultipleTenantDatabase(cs2, "query_conj_2").ForTenants("green");
            });
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.RegisterDocumentType<User>();
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
        });

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();

        foreach (var tenant in new[] { "red", "blue", "green" })
        {
            await using var session = theStore.LightweightSession(tenant);
            session.Store(new User { UserName = tenant });
            await session.SaveChangesAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        theStore?.Dispose();
        return default;
    }

    [Fact]
    public async Task a_plain_query_reads_every_tenant_in_every_database()
    {
        var users = await theStore.Advanced.QueryAcrossAllDatabasesAsync(
            async (session, ct) => await session.Query<User>().ToListAsync(ct));

        users.Select(x => x.UserName).OrderBy(x => x).ShouldBe(["blue", "green", "red"]);
    }

    [Fact]
    public async Task tenant_ids_read_only_those_tenants_in_only_their_databases()
    {
        var visited = new List<string>();
        var users = await theStore.Advanced.QueryAcrossAllDatabasesAsync(
            async (session, database, ct) =>
            {
                lock (visited) visited.Add(database.Identifier);
                return await session.Query<User>().ToListAsync(ct);
            },
            8, CancellationToken.None, "blue");

        visited.ShouldBe(["query_conj_1"]);
        users.Select(x => x.UserName).ShouldBe(["blue"]);

        users = await theStore.Advanced.QueryAcrossAllDatabasesAsync(
            async (session, ct) => await session.Query<User>().ToListAsync(ct),
            8, CancellationToken.None, "red", "green");

        users.Select(x => x.UserName).OrderBy(x => x).ShouldBe(["green", "red"]);
    }
}

public record TenantGreeted(string TenantId);

public class UsersNamed: ICompiledListQuery<User>
{
    public string Name { get; set; } = null!;

    public Expression<Func<IMartenQueryable<User>, IEnumerable<User>>> QueryIs()
    {
        return q => q.Where(x => x.UserName == Name);
    }
}

/// <summary>
///     Under sharded tenancy every document is conjoined. A query across all databases reads every tenant on
///     every shard, through LINQ, Load, compiled queries and event queries alike; the tenant id overload limits
///     both the shards it visits and the tenants it reads.
/// </summary>
[Collection("sharded-tenancy")]
public class query_across_all_sharded_databases: IAsyncLifetime
{
    private readonly ShardedTenancyFixture _fixture;
    private IDocumentStore _store = null!;
    private readonly Dictionary<string, Guid> _userIds = new();
    private readonly Dictionary<string, Guid> _streamIds = new();

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
            opts.Events.AddEventType<TenantGreeted>();
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
            var user = new User { UserName = tenantId };
            session.Store(user);
            _userIds[tenantId] = user.Id;
            _streamIds[tenantId] = session.Events.StartStream(new TenantGreeted(tenantId)).Id;
            await session.SaveChangesAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    private Task<IReadOnlyList<T>> queryAll<T>(Func<IQuerySession, CancellationToken, Task<IReadOnlyList<T>>> query)
        => _store.Advanced.QueryAcrossAllDatabasesAsync(query);

    private Task<IReadOnlyList<T>> queryTenants<T>(Func<IQuerySession, CancellationToken, Task<IReadOnlyList<T>>> query,
        params string[] tenantIds)
        => _store.Advanced.QueryAcrossAllDatabasesAsync(query, 8, CancellationToken.None, tenantIds);

    [Fact]
    public async Task a_plain_query_reads_every_tenant_on_every_shard()
    {
        var users = await queryAll(async (session, ct) => await session.Query<User>().ToListAsync(ct));

        users.Select(x => x.UserName).OrderBy(x => x)
            .ShouldBe(["q_alpha", "q_beta", "q_delta", "q_gamma"]);
    }

    [Fact]
    public async Task any_tenant_finds_every_tenants_rows_on_every_shard()
    {
        var users = await queryAll(async (session, ct) =>
            await session.Query<User>().Where(x => x.AnyTenant()).ToListAsync(ct));

        users.Select(x => x.UserName).OrderBy(x => x)
            .ShouldBe(["q_alpha", "q_beta", "q_delta", "q_gamma"]);
    }

    [Fact]
    public async Task tenant_is_one_of_in_the_query_still_applies()
    {
        var users = await queryAll(async (session, ct) =>
            await session.Query<User>().Where(x => x.TenantIsOneOf("q_beta", "q_delta")).ToListAsync(ct));

        users.Select(x => x.UserName).OrderBy(x => x).ShouldBe(["q_beta", "q_delta"]);
    }

    [Fact]
    public async Task load_by_id_reads_every_tenant()
    {
        var one = await queryAll<User>(async (session, ct) =>
            await session.LoadAsync<User>(_userIds["q_gamma"], ct) is { } user ? [user] : []);

        one.Single().UserName.ShouldBe("q_gamma");

        var many = await queryAll(async (session, ct) =>
            await session.LoadManyAsync<User>(ct, _userIds.Values));

        many.Select(x => x.UserName).OrderBy(x => x)
            .ShouldBe(["q_alpha", "q_beta", "q_delta", "q_gamma"]);
    }

    [Fact]
    public async Task compiled_queries_read_every_tenant_without_changing_ordinary_sessions()
    {
        await using (var alpha = _store.QuerySession("q_alpha"))
        {
            (await alpha.QueryAsync(new UsersNamed { Name = "q_delta" })).ShouldBeEmpty();
        }

        var users = await queryAll<User>(async (session, ct) =>
            (await session.QueryAsync(new UsersNamed { Name = "q_delta" }, ct)).ToList());

        users.Single().UserName.ShouldBe("q_delta");

        // The plan the query across databases used is not the one ordinary sessions get
        await using (var alpha = _store.QuerySession("q_alpha"))
        {
            (await alpha.QueryAsync(new UsersNamed { Name = "q_delta" })).ShouldBeEmpty();
        }

        await using (var delta = _store.QuerySession("q_delta"))
        {
            (await delta.QueryAsync(new UsersNamed { Name = "q_delta" })).Single().UserName.ShouldBe("q_delta");
        }
    }

    [Fact]
    public async Task event_queries_read_every_tenant()
    {
        var greeted = await queryAll(async (session, ct) =>
            await session.Events.QueryRawEventDataOnly<TenantGreeted>().ToListAsync(ct));

        greeted.Select(x => x.TenantId).OrderBy(x => x)
            .ShouldBe(["q_alpha", "q_beta", "q_delta", "q_gamma"]);

        var events = await queryAll(async (session, ct) =>
            await session.Events.QueryAllRawEvents().ToListAsync(ct));

        events.Select(x => x.TenantId).OrderBy(x => x)
            .ShouldBe(["q_alpha", "q_beta", "q_delta", "q_gamma"]);

        var stream = await queryAll(async (session, ct) =>
            await session.Events.FetchStreamAsync(_streamIds["q_delta"], token: ct));

        stream.Single().Data.ShouldBe(new TenantGreeted("q_delta"));
    }

    [Fact]
    public async Task tenant_ids_visit_only_the_shards_that_hold_them()
    {
        var shards = await _store.Advanced.QueryAcrossAllDatabasesAsync<string>(
            (_, database, _) => Task.FromResult<IReadOnlyList<string>>([database.Identifier]),
            8, CancellationToken.None, "q_alpha", "q_beta");

        shards.ShouldBe([_fixture.DbNames[0]]);

        shards = await _store.Advanced.QueryAcrossAllDatabasesAsync<string>(
            (_, database, _) => Task.FromResult<IReadOnlyList<string>>([database.Identifier]),
            8, CancellationToken.None, "q_alpha", "q_delta");

        shards.ShouldBe([_fixture.DbNames[0], _fixture.DbNames[2]], ignoreOrder: true);
    }

    [Fact]
    public async Task tenant_ids_read_only_those_tenants()
    {
        // q_beta shares a shard with q_alpha and is left out
        var users = await queryTenants(async (session, ct) => await session.Query<User>().ToListAsync(ct),
            "q_alpha", "q_gamma");

        users.Select(x => x.UserName).OrderBy(x => x).ShouldBe(["q_alpha", "q_gamma"]);

        var loaded = await queryTenants(async (session, ct) =>
            await session.LoadManyAsync<User>(ct, _userIds.Values), "q_alpha", "q_gamma");

        loaded.Select(x => x.UserName).OrderBy(x => x).ShouldBe(["q_alpha", "q_gamma"]);

        var greeted = await queryTenants(async (session, ct) =>
            await session.Events.QueryRawEventDataOnly<TenantGreeted>().ToListAsync(ct), "q_alpha", "q_gamma");

        greeted.Select(x => x.TenantId).OrderBy(x => x).ShouldBe(["q_alpha", "q_gamma"]);

        var betaStream = await queryTenants(async (session, ct) =>
            await session.Events.FetchStreamAsync(_streamIds["q_beta"], token: ct), "q_alpha", "q_gamma");

        betaStream.ShouldBeEmpty();
    }

    [Fact]
    public async Task an_unknown_tenant_id_is_refused_before_any_database_is_queried()
    {
        var queried = 0;

        var ex = await Should.ThrowAsync<UnknownTenantIdException>(async () =>
            await queryTenants<User>((_, _) =>
            {
                Interlocked.Increment(ref queried);
                return Task.FromResult<IReadOnlyList<User>>([]);
            }, "q_alpha", "q_nobody"));

        ex.Message.ShouldContain("q_nobody");
        queried.ShouldBe(0);
    }

    [Fact]
    public async Task no_tenant_ids_is_an_empty_result()
    {
        var users = await queryTenants<User>((_, _) => throw new InvalidOperationException("never runs"));

        users.ShouldBeEmpty();
    }
}
