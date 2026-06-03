#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.MultiTenancy;
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
/// #4617 section 3d deferred — pin that <see cref="ShardedTenancy"/> reuses
/// ONE <see cref="MartenDatabase"/> per shard across multiple tenants
/// co-located on that shard (the structural distinction from
/// MasterTableTenancy, which has one DB per tenant). The user-facing
/// consequence: <c>Disable</c>-ing tenant A on a shard shared with tenant B
/// must not dispose the shard's MartenDatabase — tenant B is still live and
/// its sessions continue to read/write through that database.
/// </summary>
[Collection("sharded-tenant-partitioned")]
public class sharded_one_database_per_shard : IAsyncLifetime
{
    private readonly ShardedPartitionedFixture _fixture;
    private IDocumentStore _store = null!;

    public sharded_one_database_per_shard(ShardedPartitionedFixture fixture)
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

    [Fact]
    public async Task two_tenants_on_the_same_shard_share_one_MartenDatabase_instance()
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
            });

            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AddEventType<ShardedTestEvent>();
        });

        // Explicitly assign two tenants to the SAME shard so we can pin the
        // one-MartenDatabase-per-shard invariant without depending on the
        // hash-distribution choice an auto-assigner would make.
        var sharedShard = _fixture.DbNames[0];
        await _store.Advanced.AddTenantToShardAsync("alpha", sharedShard, CancellationToken.None);
        await _store.Advanced.AddTenantToShardAsync("beta", sharedShard, CancellationToken.None);

        // Resolve the underlying MartenDatabase for each tenant via the
        // public Tenancy API and verify it's THE SAME OBJECT.
        var alphaTenant = await _store.Options.Tenancy.GetTenantAsync("alpha");
        var betaTenant = await _store.Options.Tenancy.GetTenantAsync("beta");

        ReferenceEquals(alphaTenant.Database, betaTenant.Database).ShouldBeTrue(
            "two tenants on the same shard must share the same MartenDatabase instance — " +
            "this is the structural distinction from MasterTableTenancy (DB-per-tenant), " +
            "and is what lets a single schema migration cover every tenant on the shard");
    }

    [Fact]
    public async Task disabling_tenant_A_on_shared_shard_does_not_break_tenant_B_appends()
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
            });

            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AddEventType<ShardedTestEvent>();
        });

        // Both tenants on the same shard.
        var sharedShard = _fixture.DbNames[1];
        await _store.Advanced.AddTenantToShardAsync("disabletest_a", sharedShard, CancellationToken.None);
        await _store.Advanced.AddTenantToShardAsync("disabletest_b", sharedShard, CancellationToken.None);

        // Seed tenant B so we have something to read back after the Disable.
        var streamId = Guid.NewGuid();
        await using (var session = _store.LightweightSession("disabletest_b"))
        {
            session.Events.StartStream(streamId, new ShardedTestEvent { Value = "before-disable" });
            await session.SaveChangesAsync();
        }

        // Disable tenant A. The shared MartenDatabase must NOT be disposed.
        var sharded = (ShardedTenancy)_store.Options.Tenancy;
        await sharded.DisableTenantAsync("disabletest_a");

        // Tenant B's sessions still work — append a fresh event and read both.
        await using (var session = _store.LightweightSession("disabletest_b"))
        {
            session.Events.Append(streamId, new ShardedTestEvent { Value = "after-disable" });
            await session.SaveChangesAsync();
        }

        await using (var query = _store.QuerySession("disabletest_b"))
        {
            var events = await query.Events.FetchStreamAsync(streamId);
            events.Count.ShouldBe(2,
                "tenant B's appends must survive a co-located tenant A Disable — the shared " +
                "MartenDatabase is reference-counted, not disposed on the first tenant's Disable");
        }
    }
}
