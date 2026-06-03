using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// Migrated from MultiTenancyTests/sharded_eager_apply_per_tenant_events_tests.cs.
/// #4606 regression — eager schema apply on a sharded + per-tenant-events store,
/// followed by runtime tenant provisioning, must succeed (no 42P16 on the partition
/// attach's FK drop).
///
/// <para>
/// The shape this exercises is the production startup pattern:
///   <c>ApplyAllConfiguredChangesToDatabaseAsync()</c> at boot → tenant arrives later
///   → <c>AddTenantToShardAsync(tenantId)</c> → first append.
/// In 9.4.x the FK on the parent <c>mt_events</c> table is inherited the moment
/// the first partition is attached, and Weasel's partition-attach drop-and-recreate
/// of that FK trips Postgres' 42P16 (cannot drop inherited constraint). #4598's
/// tests deliberately exercised the lazy-apply flow to dodge this; #4606 is the
/// eager-apply variant.
/// </para>
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class sharded_eager_apply_per_tenant_events : IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private IDocumentStore _store = null!;

    public sharded_eager_apply_per_tenant_events(ShardedPartitionedFixture fixture)
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
        _store?.Dispose();
        return Task.CompletedTask;
    }

    private IDocumentStore CreateStore()
    {
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
                x.UseSmallestDatabaseAssignment();
            });

            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AddEventType<ShardedTestEvent>();
        });

        return _store;
    }

    [Fact]
    public async Task eager_apply_then_AddTenantToShardAsync_then_append_succeeds()
    {
        CreateStore();

        // The eager-apply pattern: create the parent partitioned schema on every
        // shard at startup time, BEFORE any tenant is provisioned. This is the
        // startup-migrate deployment shape that #4606 needs to support.
        var databases = await _store.Options.Tenancy.BuildDatabases();
        foreach (var db in databases.OfType<IMartenDatabase>())
        {
            await db.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        // Runtime tenant provisioning hits the partition-attach path against an
        // already-created parent mt_events. Master fails here with
        // 42P16 cannot drop inherited constraint "mt_events_tenant_id_stream_id_fkey".
        var dbId = await _store.Advanced.AddTenantToShardAsync("alpha", CancellationToken.None);
        dbId.ShouldNotBeNull();

        // The actual user-facing failure surface: first append for the new tenant.
        // On master this throws either the same 42P16 (the attach was silently
        // swallowed and the partition is missing) or a downstream 23514 (no
        // partition of relation mt_events found for row).
        await using var session = _store.LightweightSession("alpha");
        session.Events.StartStream(Guid.NewGuid(), new ShardedTestEvent { Value = "eager-apply-then-tenant" });
        await session.SaveChangesAsync();

        // Sanity: the partition lives in the assigned shard.
        await using var conn = new NpgsqlConnection(_fixture.ConnectionStrings[dbId]);
        await conn.OpenAsync();
        var tables = await conn.ExistingTablesAsync();
        tables.Any(t => t.Name == "mt_events_alpha").ShouldBeTrue(
            $"mt_events_alpha partition must exist after eager-apply + runtime tenant. Tables: {string.Join(", ", tables.Select(t => t.QualifiedName))}");
    }
}
