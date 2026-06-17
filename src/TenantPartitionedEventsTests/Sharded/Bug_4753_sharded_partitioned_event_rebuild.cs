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
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// #4753 / #4754 — re-applying schema to a sharded, Marten-managed ByList tenant-partitioned EVENT
/// store over existing event data must be idempotent. Report: with
/// <c>Events.UseTenantPartitionedEvents = true</c> on a <c>MultiTenantedWithShardedDatabases</c> store,
/// every redeploy destructively rebuilds <c>mt_streams</c> / <c>mt_events</c> (CREATE _temp → DROP
/// CASCADE → recreate parent → INSERT…SELECT) without recreating the per-tenant LIST partitions first,
/// so the copy fails with 23514 (no partition for row).
///
/// <para>
/// This is the gap the #4706 fix left: #4706 set <c>IgnorePartitionsInMigration = true</c> on the
/// managed-partition <c>DocumentTable</c>, but the same was never applied to <c>StreamsTable</c> /
/// <c>EventsTable</c>. The sibling <see cref="Bug_4706_sharded_partitioned_doc_rebuild"/> turns the
/// events flag on but only writes DOCUMENTS — so its event tables are empty and a rebuild of them never
/// errors. This test writes EVENTS, so the rebuild's INSERT…SELECT actually has rows to fail on.
/// </para>
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class Bug_4753_sharded_partitioned_event_rebuild: IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private readonly ITestOutputHelper _output;

    public Bug_4753_sharded_partitioned_event_rebuild(ShardedPartitionedFixture fixture, ITestOutputHelper output)
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
        opts.Events.AddEventType<Bug4753ShardedEvent>();
    });

    [Fact]
    public async Task reapply_over_existing_tenant_partitioned_events_is_idempotent()
    {
        var assignment = new Dictionary<string, string>
        {
            ["tA"] = _fixture.DbNames[0],
            ["tB"] = _fixture.DbNames[1],
            ["tC"] = _fixture.DbNames[2],
        };

        // First deploy: create the parent partitioned schema on every shard, provision tenants
        // (additive per-tenant partitions on mt_streams / mt_events), then APPEND EVENTS so the
        // partitioned event tables actually hold rows.
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
                session.Events.StartStream(Guid.NewGuid(), new Bug4753ShardedEvent("e-" + tenant));
                await session.SaveChangesAsync();
            }
        }

        // Second deploy (nothing changed, fresh store): re-apply across all shards via the store-wide
        // path. Pre-fix this destructively rebuilds the partitioned mt_streams / mt_events on each shard
        // and the INSERT…SELECT of the existing events fails with 23514. With IgnorePartitionsInMigration
        // set on the event tables it is a no-op.
        await using (var store = BuildStore())
        {
            var ex = await Record.ExceptionAsync(() =>
                store.Storage.ApplyAllConfiguredChangesToDatabaseAsync());

            if (ex != null)
            {
                _output.WriteLine(ex.ToString());
            }

            ex.ShouldBeNull("Re-applying an unchanged sharded ByList tenant-partitioned event store " +
                            "must not rebuild mt_streams / mt_events / must not fail with 23514");
        }
    }
}

public record Bug4753ShardedEvent(string Label);
