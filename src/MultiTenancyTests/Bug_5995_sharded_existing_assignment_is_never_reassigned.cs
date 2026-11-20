#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Exceptions;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core.MultiTenancy;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Weasel.Postgresql.Tables;

namespace MultiTenancyTests;

/// <summary>
/// Two shard databases with unique names, shared by every test in <see cref="Bug_5995_sharded_existing_assignment_is_never_reassigned"/>
/// and dropped when the class is done. Each test isolates itself further with its own control schema and tenant ids.
/// </summary>
public class Bug5995ShardFixture: IAsyncLifetime
{
    private readonly string _prefix = "m5995_" + Guid.NewGuid().ToString("N")[..10];

    public string ShardA => _prefix + "_a";
    public string ShardB => _prefix + "_b";

    public Dictionary<string, string> ConnectionStrings { get; } = new(StringComparer.Ordinal);

    public async ValueTask InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.AdminConnectionString);
        await conn.OpenAsync();

        foreach (var name in new[] { ShardA, ShardB })
        {
            await new DatabaseSpecification().BuildDatabase(conn, name);
            ConnectionStrings[name] =
                new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString) { Database = name }.ConnectionString;
        }
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();

        await using var conn = new NpgsqlConnection(ConnectionSource.AdminConnectionString);
        await conn.OpenAsync();

        foreach (var name in new[] { ShardA, ShardB })
        {
            await conn.CreateCommand($"drop database if exists {name} with (force)").ExecuteNonQueryAsync();
        }
    }
}

public class Bug5995Doc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// #5995: a store that was not given its shard databases (no <c>AddDatabase</c>) and has not called
/// <c>BuildDatabases()</c> did not find an already-assigned tenant's shard in its local cache, re-ran the assignment
/// strategy and overwrote the assignment row, leaving the tenant's data behind on the old shard.
/// </summary>
public class Bug_5995_sharded_existing_assignment_is_never_reassigned: IClassFixture<Bug5995ShardFixture>, IAsyncLifetime
{
    private readonly Bug5995ShardFixture _fixture;
    private readonly string _controlSchema = "m5995_ctl_" + Guid.NewGuid().ToString("N")[..10];
    private readonly string _tenantId = "t5995_" + Guid.NewGuid().ToString("N")[..10];
    private readonly List<DocumentStore> _stores = new();

    public Bug_5995_sharded_existing_assignment_is_never_reassigned(Bug5995ShardFixture fixture)
    {
        _fixture = fixture;
    }

    public ValueTask InitializeAsync() => default;

    public async ValueTask DisposeAsync()
    {
        foreach (var store in _stores)
        {
            await store.DisposeAsync();
        }

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.CreateCommand($"drop schema if exists {_controlSchema} cascade").ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Always picks the same shard, and counts how often it was asked.
    /// </summary>
    private class FixedAssignment(string databaseId): ITenantAssignmentStrategy
    {
        private int _calls;
        public int Calls => _calls;

        public ValueTask<string> AssignTenantToDatabaseAsync(string tenantId, IReadOnlyList<PooledDatabase> availableDatabases)
        {
            Interlocked.Increment(ref _calls);
            return ValueTask.FromResult(databaseId);
        }
    }

    /// <param name="checkControlTables">
    /// False turns off the control tables' schema check, so the synchronous <c>GetTenant</c> can take #5986's
    /// synchronous lookup on its very first call instead of handing straight over to the asynchronous path.
    /// </param>
    private DocumentStore buildStore(bool seedPool, ITenantAssignmentStrategy? strategy = null, bool checkControlTables = true)
    {
        var store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = _controlSchema;
                x.PartitionSchemaName = "tenants";

                if (seedPool)
                {
                    foreach (var (dbName, connStr) in _fixture.ConnectionStrings)
                    {
                        x.AddDatabase(dbName, connStr);
                    }
                }

                if (strategy != null)
                {
                    x.UseCustomAssignment(strategy);
                }

                if (!checkControlTables)
                {
                    x.AutoCreate = AutoCreate.None;
                }
            });

            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.DisableNpgsqlLogging = true;
            opts.RegisterDocumentType<Bug5995Doc>();
        });

        _stores.Add(store);
        return store;
    }

    /// <summary>
    /// Store 1: seeds the pool, assigns the tenant to shard A explicitly and writes a document for it there.
    /// </summary>
    private async Task<Bug5995Doc> assignTenantToShardAAndWriteADocument()
    {
        var store = buildStore(seedPool: true);
        var tenancy = (ShardedTenancy)store.Options.Tenancy;

        await tenancy.AssignTenantAsync(_tenantId, _fixture.ShardA, CancellationToken.None);

        var doc = new Bug5995Doc { Id = Guid.NewGuid(), Name = "written on shard A" };
        await using (var session = store.LightweightSession(_tenantId))
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        (await assignedShardAsync(_tenantId)).ShouldBe(_fixture.ShardA);
        return doc;
    }

    private async Task<string?> assignedShardAsync(string tenantId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand(
            $"select database_id from {_controlSchema}.{TenantAssignmentTable.TableName} where tenant_id = :id");
        cmd.Parameters.AddWithValue("id", tenantId);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private static async Task<IMartenDatabase> resolve(DocumentStore store, string tenantId, bool sync)
    {
        var tenant = sync
            ? store.Options.Tenancy.GetTenant(tenantId)
            : await store.Options.Tenancy.GetTenantAsync(tenantId);

        return tenant.Database;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task an_assigned_tenant_resolves_to_its_shard_on_a_store_that_has_not_loaded_the_pool(bool sync)
    {
        var doc = await assignTenantToShardAAndWriteADocument();

        // Store 2: no AddDatabase, no BuildDatabases, and an assignment strategy that would move the tenant to B
        var strategy = new FixedAssignment(_fixture.ShardB);
        var store = buildStore(seedPool: false, strategy, checkControlTables: !sync);

        var database = await resolve(store, _tenantId, sync);

        (await assignedShardAsync(_tenantId)).ShouldBe(_fixture.ShardA,
            "an existing assignment must never be overwritten");
        strategy.Calls.ShouldBe(0, "the assignment strategy must not run for a tenant that already has a shard");
        database.Identifier.ShouldBe(_fixture.ShardA);

        await using var query = store.QuerySession(_tenantId);
        var loaded = await query.LoadAsync<Bug5995Doc>(doc.Id);
        loaded.ShouldNotBeNull("the tenant's document must still be readable");
        loaded.Name.ShouldBe(doc.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task an_assigned_tenant_whose_shard_left_the_pool_throws_and_keeps_its_assignment(bool sync)
    {
        await assignTenantToShardAAndWriteADocument();

        // The assignment table's foreign key to the pool normally refuses this delete. Get past it for this one
        // statement, to stand for a pool that lost the row anyway: a hand edit, a partial restore, or a control
        // database whose foreign key was never created.
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.CreateCommand("set session_replication_role = replica").ExecuteNonQueryAsync();
            await using var cmd = conn.CreateCommand(
                $"delete from {_controlSchema}.{DatabasePoolTable.TableName} where database_id = :id");
            cmd.Parameters.AddWithValue("id", _fixture.ShardA);
            (await cmd.ExecuteNonQueryAsync()).ShouldBe(1);
        }

        var strategy = new FixedAssignment(_fixture.ShardB);
        var store = buildStore(seedPool: false, strategy, checkControlTables: !sync);

        var ex = sync
            ? Should.Throw<AssignedShardNotFoundException>(() => store.Options.Tenancy.GetTenant(_tenantId))
            : await Should.ThrowAsync<AssignedShardNotFoundException>(async () =>
                await store.Options.Tenancy.GetTenantAsync(_tenantId));

        ex.TenantId.ShouldBe(_tenantId);
        ex.DatabaseId.ShouldBe(_fixture.ShardA);
        ex.Message.ShouldContain(_tenantId);
        ex.Message.ShouldContain(_fixture.ShardA);

        (await assignedShardAsync(_tenantId)).ShouldBe(_fixture.ShardA,
            "a missing shard must never cause the tenant to be reassigned");
        strategy.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task a_new_tenant_is_still_assigned_by_the_strategy(bool sync)
    {
        // Seed the pool from one store, then resolve a brand-new tenant on a store that did not seed it.
        await ((ShardedTenancy)buildStore(seedPool: true).Options.Tenancy).ListDatabasesAsync(CancellationToken.None);

        var strategy = new FixedAssignment(_fixture.ShardB);
        var store = buildStore(seedPool: false, strategy, checkControlTables: !sync);

        var database = await resolve(store, _tenantId, sync);

        strategy.Calls.ShouldBe(1);
        (await assignedShardAsync(_tenantId)).ShouldBe(_fixture.ShardB);
        database.Identifier.ShouldBe(_fixture.ShardB);

        var doc = new Bug5995Doc { Id = Guid.NewGuid(), Name = "new tenant" };
        await using (var session = store.LightweightSession(_tenantId))
        {
            session.Store(doc);
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession(_tenantId);
        (await query.LoadAsync<Bug5995Doc>(doc.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task two_stores_racing_to_assign_the_same_new_tenant_agree_on_one_shard()
    {
        await ((ShardedTenancy)buildStore(seedPool: true).Options.Tenancy).ListDatabasesAsync(CancellationToken.None);

        // Each store's strategy would pick a different shard, so only the assignment guard keeps them together
        var toA = new FixedAssignment(_fixture.ShardA);
        var toB = new FixedAssignment(_fixture.ShardB);
        var storeA = buildStore(seedPool: false, toA);
        var storeB = buildStore(seedPool: false, toB);

        var resolved = await Task.WhenAll(
            Task.Run(async () => (await storeA.Options.Tenancy.GetTenantAsync(_tenantId)).Database.Identifier),
            Task.Run(async () => (await storeB.Options.Tenancy.GetTenantAsync(_tenantId)).Database.Identifier));

        var assigned = await assignedShardAsync(_tenantId);
        assigned.ShouldNotBeNull();
        resolved[0].ShouldBe(assigned);
        resolved[1].ShouldBe(assigned);
        (toA.Calls + toB.Calls).ShouldBe(1, "only one of the two stores may run its assignment strategy");
    }
}
