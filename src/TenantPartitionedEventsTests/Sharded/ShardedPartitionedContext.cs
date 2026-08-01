#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Npgsql;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Sharded;

/// <summary>
/// Base for test classes on the "sharded-tenant-partitioned" collection.
/// Owns the per-test scrub that every class used to re-declare (drop the
/// master <c>sharded</c> schema, then per shard: drop <c>tenants</c> and wipe
/// every <c>mt_*</c> object from public), plus tracked disposal for however
/// many stores a test builds.
/// </summary>
/// <remarks>
/// Subclasses still carry their own <c>[Collection("sharded-tenant-partitioned")]</c>
/// attribute — xUnit reads it from the concrete class. Stores built through
/// <see cref="BuildShardedStore"/> get the standard three-shard wiring
/// (master schema <c>sharded</c>, partition schema <c>tenants</c>, smallest-
/// database assignment) and the common partitioned-event config; the
/// configure callback runs last so files can override or extend any of it.
/// AutoCreateSchemaObjects is deliberately NOT set here — files split
/// between All and the default. Tests whose store wiring deviates from the
/// standard block (per-database partitioning, custom assignment...) should
/// build their own store and hand it to <see cref="TrackStore"/> for
/// disposal.
/// </remarks>
public abstract class ShardedPartitionedContext: IAsyncLifetime
{
    private readonly List<IDocumentStore> _stores = new();

    protected ShardedPartitionedContext(ShardedPartitionedFixture fixture)
    {
        Fixture = fixture;
    }

    protected ShardedPartitionedFixture Fixture { get; }

    public virtual async ValueTask InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync("sharded"); } catch { }

        foreach (var connStr in Fixture.ConnectionStrings.Values)
        {
            await using var tenantConn = new NpgsqlConnection(connStr);
            await tenantConn.OpenAsync();
            try { await tenantConn.DropSchemaAsync("tenants"); } catch { }
            await ShardedPartitionedFixture.CleanMartenObjectsInPublicSchema(tenantConn);
        }
    }

    /// <summary>
    /// Track an externally built store for disposal at class teardown.
    /// </summary>
    protected T TrackStore<T>(T store) where T : IDocumentStore
    {
        _stores.Add(store);
        return store;
    }

    /// <summary>
    /// Build (and track) a store with the standard three-shard wiring and the
    /// common partitioned-event config. <paramref name="configure"/> runs
    /// last, so it can add event types/projections or override any common
    /// line.
    /// </summary>
    protected DocumentStore BuildShardedStore(Action<StoreOptions>? configure = null)
    {
        var store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                x.ConnectionString = ConnectionSource.ConnectionString;
                x.SchemaName = "sharded";
                x.PartitionSchemaName = "tenants";
                foreach (var (dbName, connStr) in Fixture.ConnectionStrings)
                {
                    x.AddDatabase(dbName, connStr);
                }
                x.UseSmallestDatabaseAssignment();
            });

            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;

            configure?.Invoke(opts);
        });

        return TrackStore(store);
    }

    public virtual ValueTask DisposeAsync()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        _stores.Clear();

        return default;
    }
}
