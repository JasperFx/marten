#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Sharded;

public class Bug4944Alpha
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class Bug4944Beta
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class Bug4944Gamma
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// #4944 — <c>AddPartitionToAllTables</c> decided which tables needed a new tenant's list partition by
/// walking the CALLING store's <c>StoreOptions</c>, so "all tables" really meant "all tables this store
/// happens to know about". A provisioning tool that doesn't register the application's full document
/// graph — a conversion tool, an admin CLI, a seeding job, all of which typically register only the
/// event types they need — therefore reported success while creating partitions for only a SUBSET of the
/// partitioned tables. The registry row and the event partitions landed, the tenant looked provisioned,
/// and the damage surfaced later as <c>23514 no partition of relation ...</c> on the APPLICATION's first
/// document write. Found by @erdtsieck at 512 tenant databases, where the tool created zero of the
/// application's 71 partitioned document tables (see #4943).
///
/// The fix makes the sweep database-driven: enumerate the tenant-list-partitioned parents from the
/// PostgreSQL catalog and partition every one of them, scoped to the schemas the store owns.
///
/// The whole point of these tests is that provisioning happens from a store with a DELIBERATELY
/// INCOMPLETE registration — a test that provisions from a fully-registered store proves nothing.
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class Bug_4944_database_driven_partition_sweep : IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private readonly List<IDocumentStore> _stores = new();

    public Bug_4944_database_driven_partition_sweep(ShardedPartitionedFixture fixture)
    {
        _fixture = fixture;
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

            // The fixture's cleaner only knows how to drop mt_* objects; these tests plant
            // non-Marten partitioned tables to prove the sweep does NOT touch them.
            try { await tenantConn.DropSchemaAsync("foreign_app"); } catch { }
            await tenantConn.CreateCommand("drop table if exists public.other_app_ledger cascade")
                .ExecuteNonQueryAsync();
        }
    }

    public Task DisposeAsync()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The APPLICATION store: the full document graph, every doc type tenant-list-partitioned.
    /// </summary>
    private IDocumentStore BuildApplicationStore()
    {
        return buildStore(opts =>
        {
            opts.Schema.For<Bug4944Alpha>().MultiTenantedWithPartitioning(x => x.ByList());
            opts.Schema.For<Bug4944Beta>().MultiTenantedWithPartitioning(x => x.ByList());
            opts.Schema.For<Bug4944Gamma>().MultiTenantedWithPartitioning(x => x.ByList());
        });
    }

    /// <summary>
    /// The PROVISIONING TOOL store: the reporter's shape — same physical databases, same sharded
    /// tenancy, same per-tenant event partitioning, but it registers NOT ONE of the application's
    /// document types. Pre-fix, provisioning from this store created zero document partitions.
    /// </summary>
    private IDocumentStore BuildToolStore()
    {
        return buildStore(_ => { });
    }

    private IDocumentStore buildStore(Action<StoreOptions> configure)
    {
        var store = DocumentStore.For(opts =>
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

                x.UseSmallestDatabaseAssignment();
            });

            opts.AutoCreateSchemaObjects = AutoCreate.All;

            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AddEventType<ShardedTestEvent>();

            configure(opts);
        });

        _stores.Add(store);
        return store;
    }

    /// <summary>
    /// Production eager-apply shape: the application creates its partitioned parent tables on every
    /// shard (with zero partitions — no tenants yet) before any tenant is provisioned.
    /// </summary>
    private async Task applyApplicationSchemaAsync(IDocumentStore store)
    {
        var databases = await store.Options.Tenancy.BuildDatabases();
        foreach (var db in databases.OfType<IMartenDatabase>())
        {
            await db.ApplyAllConfiguredChangesToDatabaseAsync();
        }
    }

    [Fact]
    public async Task tool_store_with_no_document_types_still_provisions_every_partitioned_table()
    {
        var tenantId = "t4944_alpha";

        var app = BuildApplicationStore();
        await applyApplicationSchemaAsync(app);

        // The provisioning tool knows nothing about Alpha/Beta/Gamma. Pre-fix it created partitions
        // for exactly zero of them and still reported success.
        var tool = BuildToolStore();
        var dbId = await tool.Advanced.AddTenantToShardAsync(tenantId, CancellationToken.None);
        dbId.ShouldNotBeNull();

        await assertPartitionExists(dbId, $"mt_doc_bug4944alpha_{tenantId}");
        await assertPartitionExists(dbId, $"mt_doc_bug4944beta_{tenantId}");
        await assertPartitionExists(dbId, $"mt_doc_bug4944gamma_{tenantId}");

        // The acceptance test the reporter actually cared about: the APPLICATION can now write every
        // document type for the tenant the TOOL provisioned. Pre-fix each of these failed with
        // 23514 'no partition of relation "mt_doc_bug4944alpha" found for row'.
        await using (var session = app.LightweightSession(tenantId))
        {
            session.Store(new Bug4944Alpha { Id = Guid.NewGuid(), Name = "a" });
            session.Store(new Bug4944Beta { Id = Guid.NewGuid(), Name = "b" });
            session.Store(new Bug4944Gamma { Id = Guid.NewGuid(), Name = "g" });
            session.Events.StartStream(Guid.NewGuid(), new ShardedTestEvent { Value = "e" });
            await session.SaveChangesAsync();
        }

        await using (var query = app.QuerySession(tenantId))
        {
            (await query.Query<Bug4944Alpha>().CountAsync()).ShouldBe(1);
            (await query.Query<Bug4944Beta>().CountAsync()).ShouldBe(1);
            (await query.Query<Bug4944Gamma>().CountAsync()).ShouldBe(1);
        }
    }

    [Fact]
    public async Task partially_registered_store_still_provisions_the_types_it_does_not_know()
    {
        var tenantId = "t4944_bravo";

        var app = BuildApplicationStore();
        await applyApplicationSchemaAsync(app);

        // A store that registers a SUBSET — Alpha only. Beta and Gamma are the ones that must be
        // rescued by the database-driven sweep rather than by the caller's registrations.
        var partial = buildStore(opts =>
        {
            opts.Schema.For<Bug4944Alpha>().MultiTenantedWithPartitioning(x => x.ByList());
        });

        var dbId = await partial.Advanced.AddTenantToShardAsync(tenantId, CancellationToken.None);

        await assertPartitionExists(dbId, $"mt_doc_bug4944alpha_{tenantId}");
        await assertPartitionExists(dbId, $"mt_doc_bug4944beta_{tenantId}");
        await assertPartitionExists(dbId, $"mt_doc_bug4944gamma_{tenantId}");
    }

    /// <summary>
    /// Pins the pre-#4944 behavior behind the opt-out, which doubles as proof that these tests
    /// genuinely exercise the new sweep: with the sweep disabled, the tool store under-provisions
    /// exactly as reported.
    /// </summary>
    [Fact]
    public async Task opting_out_of_the_sweep_restores_the_old_under_provisioning_behavior()
    {
        var tenantId = "t4944_charlie";

        var app = BuildApplicationStore();
        await applyApplicationSchemaAsync(app);

        // Same tool store, but with the database-driven sweep turned off. Constructing the manager
        // explicitly is exactly what StoreOptions does for UseTenantPartitionedEvents when the user
        // hasn't already set one up, and it's the supported way to reach the toggle at config time.
        var tool = buildStore(opts =>
        {
            var partitions = new MartenManagedTenantListPartitions(opts, schemaName: null);
            partitions.Partitions.SweepPartitionedTablesFromDatabase = false;
        });

        var dbId = await tool.Advanced.AddTenantToShardAsync(tenantId, CancellationToken.None);

        // The registry row and the event partitions land (that always worked) ...
        await assertPartitionExists(dbId, $"mt_events_{tenantId}");

        // ... but not one of the document tables the tool doesn't know about. This is the bug.
        await assertPartitionMissing(dbId, $"mt_doc_bug4944alpha_{tenantId}");
        await assertPartitionMissing(dbId, $"mt_doc_bug4944beta_{tenantId}");
        await assertPartitionMissing(dbId, $"mt_doc_bug4944gamma_{tenantId}");
    }

    /// <summary>
    /// The scoping guarantee. A shard database is routinely shared with other applications, and a
    /// sweep that grabbed every list-partitioned table it could see would start bolting Marten tenant
    /// partitions onto a stranger's tables. Two decoys, both shaped to be swept if the filters were
    /// sloppy:
    ///   * <c>foreign_app.their_ledger</c> — the EXACT shape the sweep looks for (LIST partitioned on a
    ///     single <c>tenant_id</c> column), but in a schema the store does not own.
    ///   * <c>public.other_app_ledger</c> — sitting in a schema the store DOES own, but list-partitioned
    ///     on a non-tenant column, so it is not a tenant-partitioned table at all.
    /// Neither may be touched.
    /// </summary>
    [Fact]
    public async Task never_touches_partitioned_tables_outside_the_stores_schemas_or_shape()
    {
        var tenantId = "t4944_delta";

        foreach (var connStr in _fixture.ConnectionStrings.Values)
        {
            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync();

            await conn.CreateCommand("create schema if not exists foreign_app").ExecuteNonQueryAsync();
            await conn.CreateCommand(
                    "create table foreign_app.their_ledger (tenant_id varchar not null, amount int) partition by list (tenant_id)")
                .ExecuteNonQueryAsync();

            await conn.CreateCommand(
                    "create table public.other_app_ledger (category varchar not null, amount int) partition by list (category)")
                .ExecuteNonQueryAsync();
        }

        var app = BuildApplicationStore();
        await applyApplicationSchemaAsync(app);

        var tool = BuildToolStore();
        var dbId = await tool.Advanced.AddTenantToShardAsync(tenantId, CancellationToken.None);

        // The store's own tables were still swept — the scoping is narrow, not broken.
        await assertPartitionExists(dbId, $"mt_doc_bug4944alpha_{tenantId}");

        // A foreign schema is never swept, even though its table is the exact shape we look for.
        await assertNoPartitionsOf(dbId, "foreign_app", "their_ledger");

        // A table in OUR schema that isn't tenant-partitioned is never swept either.
        await assertNoPartitionsOf(dbId, "public", "other_app_ledger");
    }

    // ---- helpers ----

    private async Task assertPartitionExists(string dbId, string partitionTableName)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionStrings[dbId]);
        await conn.OpenAsync();
        var tables = await conn.ExistingTablesAsync();
        tables.Any(t => t.Schema == "public" && t.Name == partitionTableName).ShouldBeTrue(
            $"list partition '{partitionTableName}' must exist in shard '{dbId}'. Tables: {string.Join(", ", tables.Select(t => t.QualifiedName))}");
    }

    private async Task assertPartitionMissing(string dbId, string partitionTableName)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionStrings[dbId]);
        await conn.OpenAsync();
        var tables = await conn.ExistingTablesAsync();
        tables.Any(t => t.Schema == "public" && t.Name == partitionTableName).ShouldBeFalse(
            $"list partition '{partitionTableName}' must NOT exist in shard '{dbId}'");
    }

    /// <summary>
    /// Assert the given partitioned parent has no child partitions at all — i.e. Marten never went
    /// anywhere near it.
    /// </summary>
    private async Task assertNoPartitionsOf(string dbId, string schema, string tableName)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionStrings[dbId]);
        await conn.OpenAsync();

        var count = (long)(await conn.CreateCommand(
                """
                select count(*)
                from pg_catalog.pg_inherits i
                join pg_catalog.pg_class parent on parent.oid = i.inhparent
                join pg_catalog.pg_namespace n on n.oid = parent.relnamespace
                where n.nspname = :schema and parent.relname = :table
                """)
            .With("schema", schema)
            .With("table", tableName)
            .ExecuteScalarAsync())!;

        count.ShouldBe(0L,
            $"the tenant-partition sweep must never add partitions to '{schema}.{tableName}' in shard '{dbId}'");
    }
}
