#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Sharded;

public class Bug4706Doc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// #4706 — re-applying schema to a sharded, Marten-managed ByList tenant-partitioned document table
/// over existing data must be idempotent. Report: every redeploy destructively rebuilds the
/// partitioned doc tables (CREATE _temp -> INSERT ... SELECT -> drop/rename) without recreating the
/// per-tenant LIST partitions first, so the copy fails with 23514 (no partition for row). A fresh DB
/// is fine. A single-DB managed-ByList store is idempotent; this exercises the sharded path.
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class Bug_4706_sharded_partitioned_doc_rebuild: IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private readonly ITestOutputHelper _output;

    public Bug_4706_sharded_partitioned_doc_rebuild(ShardedPartitionedFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync("sharded"); } catch { }
        foreach (var connStr in _fixture.ConnectionStrings.Values)
        {
            await using var tenantConn = new NpgsqlConnection(connStr);
            await tenantConn.OpenAsync();
            try { await tenantConn.DropSchemaAsync("tenants"); } catch { }
            await ShardedPartitionedFixture.CleanMartenObjectsInPublicSchema(tenantConn);
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DocumentStore BuildStore() => (DocumentStore)DocumentStore.For(opts =>
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

        opts.AutoCreateSchemaObjects = AutoCreate.All;
        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.UseTenantPartitionedEvents = true;

        opts.Schema.For<Bug4706Doc>()
            .MultiTenantedWithPartitioning(x => x.ByList())
            .Index(x => x.Name)
            .StartIndexesByTenantId();
    });

    [Fact]
    public async Task reapply_over_existing_tenant_data_is_idempotent()
    {
        var assignment = new Dictionary<string, string>
        {
            ["tA"] = _fixture.DbNames[0],
            ["tB"] = _fixture.DbNames[1],
            ["tC"] = _fixture.DbNames[2],
        };

        // First deploy: production eager-apply shape — create the parent partitioned
        // schema on every shard up front (per database, sequentially), THEN provision
        // tenants (which adds each tenant's partition to its shard via the additive
        // path), THEN write data.
        await using (var store = BuildStore())
        {
            var databases = await store.Options.Tenancy.BuildDatabases();
            foreach (var db in databases.OfType<IMartenDatabase>())
            {
                await db.ApplyAllConfiguredChangesToDatabaseAsync();
            }

            foreach (var (tenant, shard) in assignment)
            {
                await store.Advanced.AddTenantToShardAsync(tenant, shard, CancellationToken.None);
            }

            foreach (var tenant in assignment.Keys)
            {
                await using var session = store.LightweightSession(tenant);
                session.Store(new Bug4706Doc { Id = Guid.NewGuid(), Name = "n-" + tenant });
                await session.SaveChangesAsync();
            }
        }

        // Second deploy (nothing changed): re-apply across all shards via the store-wide
        // path (Parallel.ForEachAsync over databases — the #4706 trigger). Must be
        // idempotent: no destructive rebuild of the per-tenant partitioned tables, no 23514.
        await using (var store = BuildStore())
        {
            var ex = await Record.ExceptionAsync(() =>
                store.Storage.ApplyAllConfiguredChangesToDatabaseAsync());

            if (ex != null)
            {
                _output.WriteLine(ex.ToString());
            }

            ex.ShouldBeNull("Re-applying an unchanged sharded ByList tenant-partitioned doc table " +
                            "must not rebuild it / must not fail with 23514");
        }
    }
}
