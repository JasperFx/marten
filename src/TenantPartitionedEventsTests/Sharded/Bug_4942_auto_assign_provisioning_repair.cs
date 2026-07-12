#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Sharded;

public class Bug4942Doc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// #4942 — the auto-assign path (<c>AddTenantToShardAsync(tenantId, ct)</c> →
/// <c>findOrAssignTenantDatabaseAsync</c>) returned early as soon as an assignment row existed,
/// skipping <c>createPartitionsForTenant</c> + <c>PerTenantEventSequences.EnsureSequencesAsync</c>
/// entirely. A half-provisioned tenant (assignment row committed, partition DDL failed or
/// interrupted — the crash window the "outside the advisory lock" comment documents) was therefore
/// NEVER repaired by auto-assign: every write to a missing document partition failed with 23514
/// forever, while the explicit <c>AddTenantToShardAsync(tenantId, databaseId)</c> overload always
/// re-ran the idempotent repair. Reported from production: 71 partitioned document tables missing
/// one tenant's list partitions ran silently broken for two days (see also #4941).
///
/// The fix runs the same idempotent repair on the existing-assignment paths, guarded to first
/// sight of the tenant per process (the <c>_tenantToDatabase</c> cache), so the hot path pays at
/// most one batch of IF NOT EXISTS catalog checks per tenant per process.
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class Bug_4942_auto_assign_provisioning_repair : IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private readonly List<IDocumentStore> _stores = new();

    public Bug_4942_auto_assign_provisioning_repair(ShardedPartitionedFixture fixture)
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

    private IDocumentStore BuildStore()
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

            opts.Schema.For<Bug4942Doc>()
                .MultiTenantedWithPartitioning(x => x.ByList());
        });

        _stores.Add(store);
        return store;
    }

    /// <summary>
    /// Provision the tenant end to end on a first store ("process 1"), then damage it into the
    /// exact half-provisioned state from the report: assignment row intact in the master
    /// registry, but the tenant's document list partition and per-tenant event sequence gone
    /// from its shard. Returns the assigned database id.
    /// </summary>
    private async Task<string> provisionThenDamageAsync(string tenantId)
    {
        var store = BuildStore();

        // Production eager-apply shape (same as Bug_4706) — parent partitioned tables
        // exist on every shard before any tenant is provisioned.
        var databases = await store.Options.Tenancy.BuildDatabases();
        foreach (var db in databases.OfType<IMartenDatabase>())
        {
            await db.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        var dbId = await store.Advanced.AddTenantToShardAsync(tenantId, CancellationToken.None);
        dbId.ShouldNotBeNull();

        // Fully provisioned: document partition + per-tenant event sequence both exist.
        await assertDocPartitionExists(dbId, tenantId);
        await assertSequenceState(dbId, tenantId, shouldExist: true, "after initial provisioning");

        store.Dispose();
        _stores.Remove(store);

        await damageTenantAsync(dbId, tenantId);
        return dbId;
    }

    private async Task damageTenantAsync(string dbId, string tenantId)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionStrings[dbId]);
        await conn.OpenAsync();
        await conn.CreateCommand($"drop table if exists public.mt_doc_bug4942doc_{tenantId}")
            .ExecuteNonQueryAsync();
        await conn.CreateCommand($"drop sequence if exists public.mt_events_sequence_{tenantId}")
            .ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task auto_assign_overload_repairs_half_provisioned_tenant_in_new_process()
    {
        var tenantId = "t4942_kilo";
        var dbId = await provisionThenDamageAsync(tenantId);

        // "Process 2": a fresh store (cold tenant cache) calls the AUTO-ASSIGN overload — the
        // one the reporter's conversion tool used. On master this returned early off the
        // existing assignment row and never repaired anything.
        var store2 = BuildStore();
        var dbId2 = await store2.Advanced.AddTenantToShardAsync(tenantId, CancellationToken.None);
        dbId2.ShouldBe(dbId);

        // The idempotent repair must have recreated both halves of the damage.
        await assertDocPartitionExists(dbId, tenantId);
        await assertSequenceState(dbId, tenantId, shouldExist: true, "auto-assign must repair the per-tenant event sequence");

        // And the tenant is actually writable again — pre-fix this Store/SaveChanges failed
        // with 23514 'no partition of relation "mt_doc_bug4942doc"'.
        await using (var session = store2.LightweightSession(tenantId))
        {
            session.Store(new Bug4942Doc { Id = Guid.NewGuid(), Name = "healed" });
            session.Events.StartStream(Guid.NewGuid(), new ShardedTestEvent { Value = "healed" });
            await session.SaveChangesAsync();
        }

        await using (var query = store2.QuerySession(tenantId))
        {
            (await query.Query<Bug4942Doc>().CountAsync()).ShouldBe(1);
        }
    }

    [Fact]
    public async Task lazy_tenant_resolution_repairs_on_first_sight()
    {
        var tenantId = "t4942_lima";
        var dbId = await provisionThenDamageAsync(tenantId);

        // "Process 2": plain tenant resolution (what every session opening does) — the purest
        // findOrAssignTenantDatabaseAsync entry. No writes here, so nothing else (e.g. a lazy
        // schema apply) can mask whether the resolution itself did the repair.
        var store2 = BuildStore();
        await store2.Options.Tenancy.GetTenantAsync(tenantId);

        await assertDocPartitionExists(dbId, tenantId);
        await assertSequenceState(dbId, tenantId, shouldExist: true, "first-sight tenant resolution must repair the sequence");
    }

    [Fact]
    public async Task repair_runs_at_most_once_per_process_per_tenant()
    {
        var tenantId = "t4942_mike";
        var store = BuildStore();

        var dbId = await store.Advanced.AddTenantToShardAsync(tenantId, CancellationToken.None);
        await assertSequenceState(dbId, tenantId, shouldExist: true, "after initial provisioning");

        // Damage AFTER the tenant is already in this process's cache.
        await damageTenantAsync(dbId, tenantId);

        // Second auto-assign call in the SAME process: the tenant is in _tenantToDatabase, so
        // the first-sight guard must skip the repair — no shard DDL round trip at all. The
        // still-missing sequence is the direct observable for "no repair work happened".
        await store.Advanced.AddTenantToShardAsync(tenantId, CancellationToken.None);
        await assertSequenceState(dbId, tenantId, shouldExist: false,
            "the once-per-process guard must skip the repair for an already-cached tenant");

        // A fresh process DOES heal it — the guard defers the repair, it doesn't lose it.
        var store2 = BuildStore();
        await store2.Advanced.AddTenantToShardAsync(tenantId, CancellationToken.None);
        await assertSequenceState(dbId, tenantId, shouldExist: true, "a new process must repair on first sight");
    }

    [Fact]
    public async Task explicit_overload_repairs_even_when_tenant_is_cached()
    {
        var tenantId = "t4942_nov";
        var store = BuildStore();

        var databases = await store.Options.Tenancy.BuildDatabases();
        foreach (var db in databases.OfType<IMartenDatabase>())
        {
            await db.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        var dbId = await store.Advanced.AddTenantToShardAsync(tenantId, CancellationToken.None);
        await assertDocPartitionExists(dbId, tenantId);

        await damageTenantAsync(dbId, tenantId);

        // The explicit-target overload documents "a re-run ... completes the tenant" — it must
        // keep repairing unconditionally, even in the same process with a warm cache (unlike
        // the guarded auto-assign path above). Pins the pre-#4942 contract.
        await store.Advanced.AddTenantToShardAsync(tenantId, dbId, CancellationToken.None);

        await assertDocPartitionExists(dbId, tenantId);
        await assertSequenceState(dbId, tenantId, shouldExist: true, "explicit re-assignment must always re-run the repair");
    }

    // ---- helpers ----

    private async Task assertDocPartitionExists(string dbId, string tenantId)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionStrings[dbId]);
        await conn.OpenAsync();
        var tables = await conn.ExistingTablesAsync();
        tables.Any(t => t.Name == $"mt_doc_bug4942doc_{tenantId}").ShouldBeTrue(
            $"list partition 'mt_doc_bug4942doc_{tenantId}' must exist in shard '{dbId}'. Tables: {string.Join(", ", tables.Select(t => t.QualifiedName))}");
    }

    private async Task assertSequenceState(string dbId, string tenantId, bool shouldExist, string because)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionStrings[dbId]);
        await conn.OpenAsync();
        var count = (long)(await conn.CreateCommand(
                "select count(*) from pg_sequences where schemaname = 'public' and sequencename = :n")
            .With("n", $"mt_events_sequence_{tenantId}")
            .ExecuteScalarAsync())!;
        var expected = shouldExist ? 1L : 0L;
        count.ShouldBe(expected,
            $"sequence 'mt_events_sequence_{tenantId}' in shard '{dbId}': {because}");
    }
}
