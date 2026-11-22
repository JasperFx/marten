using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Xunit;

namespace EventSourcingTests.FetchForWriting;

/// <summary>
/// Natural keys in store shapes the fetch matrix does not cover: composite projections, global aggregates in a
/// conjoined store, the write cache's pointer limit, an Async aggregate without revisions, and the
/// auto-registration of an aggregate that only has mapped keys.
/// </summary>
public class natural_key_store_shapes: OneOffConfigurationsContext
{
    private readonly MartenTestAggregateWriteCache theCache = new();

    private async Task<Guid> startOrder(OrderNumber number, string tenantId = StorageConstants.DefaultTenantId,
        params object[] more)
    {
        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession(tenantId);
        session.Events.StartStream<OrderAggregate>(streamId, [new OrderCreated(number, "Alice"), ..more]);
        await session.SaveChangesAsync();
        return streamId;
    }

    [Fact]
    public void an_owned_key_in_a_composite_projection_is_refused_when_the_store_is_built()
    {
        var ex = Should.Throw<InvalidProjectionException>(() =>
        {
            using var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.Projections.CompositeProjectionFor("Orders", c => c.Snapshot<OrderAggregate>());
            });
        });

        ex.Message.ShouldContain("OrderAggregate.OrderNum is an owned natural key");
        ex.Message.ShouldContain("the composite projection 'Orders'");
    }

    [Fact]
    public async Task a_mapped_key_in_a_composite_projection_reads_its_owners_lookup()
    {
        StoreOptions(opts =>
        {
            #region sample_mapped_natural_key_in_a_composite_projection

            // The owner of the order number key is registered on its own...
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);

            // ...so a member of a composite projection can be fetched by that key through a mapped key
            opts.Projections.CompositeProjectionFor("Summaries", c =>
            {
                c.Snapshot<OrderSummary>();
                c.Snapshot<SimpleAggregate>();
            });

            #endregion
        });

        var number = new OrderNumber("ORD-COMPOSITE");
        var streamId = await startOrder(number, more: [new OrderItemAdded("Widget", 1m)]);

        await using var session = theStore.LightweightSession();

        var summary = await session.Events.FetchLatest<OrderSummary, OrderNumber>(number);
        summary.ShouldNotBeNull();
        summary.Id.ShouldBe(streamId);
        summary.Items.ShouldBe(1);

        var stream = await session.Events.FetchForWriting<OrderSummary, OrderNumber>(number);
        stream.Id.ShouldBe(streamId);
        stream.CurrentVersion.ShouldBe(2);
    }

    [Theory]
    [InlineData(ProjectionLifecycle.Inline)]
    [InlineData(ProjectionLifecycle.Async)]
    public async Task a_global_aggregate_in_a_conjoined_store_is_found_by_key_from_any_tenant(
        ProjectionLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Projections.AddGlobalProjection(new SingleStreamProjection<OrderAggregate, Guid>(), lifecycle);
        });

        var number = new OrderNumber("ORD-GLOBAL");
        var streamId = await startOrder(number, "red");

        await using (var blue = theStore.LightweightSession("blue"))
        {
            var stream = await blue.Events.FetchForWriting<OrderAggregate, OrderNumber>(number);
            stream.Id.ShouldBe(streamId);
            stream.CurrentVersion.ShouldBe(1);
            stream.AppendOne(new OrderItemAdded("Widget", 5m));
            await blue.SaveChangesAsync();
        }

        await using (var red = theStore.LightweightSession("red"))
        {
            var order = await red.Events.FetchLatest<OrderAggregate, OrderNumber>(number);
            order!.Id.ShouldBe(streamId);
            order.TotalAmount.ShouldBe(5m);
        }

        // One set of keys for the whole store, so another tenant cannot claim the key again
        await using var green = theStore.LightweightSession("green");
        green.Events.StartStream<OrderAggregate>(Guid.NewGuid(), new OrderCreated(number, "Bob"));
        await Should.ThrowAsync<DuplicateNaturalKeyException>(() => green.SaveChangesAsync());
    }

    [Fact]
    public async Task a_key_whose_cache_pointer_was_evicted_still_finds_its_stream()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
            opts.Events.AggregateWriteCaching.Cache = theCache;

            // Bounds the pointers from a key to its stream. The test cache itself keeps every aggregate.
            opts.Events.AggregateWriteCaching.SizeLimit = 2;
            opts.Events.CacheAggregatesForWriting<OrderAggregate>();
        });

        var numbers = new[] { "ORD-P1", "ORD-P2", "ORD-P3", "ORD-P4" };
        var streams = new Dictionary<string, Guid>();
        foreach (var value in numbers)
        {
            var number = new OrderNumber(value);
            streams[value] = await startOrder(number);

            // Written back to the cache after the commit, which points the key at the stream
            await using var writing = theStore.LightweightSession();
            var stream = await writing.Events.FetchForWriting<OrderAggregate>(streams[value]);
            stream.AppendOne(new OrderItemAdded("Widget", 1m));
            await writing.SaveChangesAsync();
        }

        // Only the two pointers written last are kept. The others are resolved in the database, as a fetch with no
        // cache is.
        await using var session = theStore.LightweightSession();
        foreach (var value in numbers)
        {
            var stream = await session.Events.FetchForWriting<OrderAggregate, OrderNumber>(new OrderNumber(value));

            stream.Id.ShouldBe(streams[value]);
            stream.CurrentVersion.ShouldBe(2);
            stream.Aggregate!.TotalAmount.ShouldBe(1m);
        }
    }

    [Fact]
    public async Task an_async_aggregate_without_revisions_is_fetched_by_key()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Async);
            opts.Schema.For<OrderAggregate>().UseNumericRevisions(false);
        });

        var number = new OrderNumber("ORD-NO-REVISIONS");
        var streamId = await startOrder(number, more: [new OrderItemAdded("Widget", 3m)]);

        await using var session = theStore.LightweightSession();
        var stream = await session.Events.FetchForWriting<OrderAggregate, OrderNumber>(number);

        stream.Id.ShouldBe(streamId);
        stream.CurrentVersion.ShouldBe(2);
        stream.Aggregate!.TotalAmount.ShouldBe(3m);
    }

    [Fact]
    public async Task an_unregistered_aggregate_with_only_a_mapped_key_is_not_registered_as_its_owner()
    {
        StoreOptions(opts => opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline));

        var number = new OrderNumber("ORD-UNREGISTERED");
        await startOrder(number);

        // OrderSummary only maps OrderAggregate's key, so the auto-registration of self-aggregating key owners leaves
        // it alone, and with no projection of its own there is nothing to fetch it with
        await using var session = theStore.LightweightSession();
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => session.Events.FetchLatest<OrderSummary, OrderNumber>(number).AsTask());

        ex.Message.ShouldContain("Unable to determine a fetch plan for aggregate");
        ex.Message.ShouldContain(nameof(OrderSummary));
    }
}

/// <summary>
/// A database per tenant: each database has its own lookup, and a write cache pointer from a key to a stream is
/// kept per database.
/// </summary>
public class natural_keys_with_a_database_per_tenant: IAsyncLifetime
{
    private static readonly string[] TenantDatabases = ["natural_keys_north", "natural_keys_south"];
    private const string SchemaName = "natural_keys_per_database";

    private readonly Dictionary<string, string> _connectionStrings = new();
    private readonly MartenTestAggregateWriteCache theCache = new();
    private IDocumentStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        // Two separate physical databases, so the lookups cannot agree by sharing a table
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            foreach (var db in TenantDatabases)
            {
                if (!await conn.DatabaseExists(db))
                {
                    await new DatabaseSpecification().BuildDatabase(conn, db);
                }

                var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString) { Database = db };
                _connectionStrings[db] = builder.ConnectionString;
            }
        }

        // The databases outlive the run, so start from an empty schema
        foreach (var connectionString in _connectionStrings.Values)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"drop schema if exists {SchemaName} cascade";
            await cmd.ExecuteNonQueryAsync();
        }

        _store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedDatabases(x =>
            {
                x.AddSingleTenantDatabase(_connectionStrings[TenantDatabases[0]], "north");
                x.AddSingleTenantDatabase(_connectionStrings[TenantDatabases[1]], "south");
            });
            opts.DatabaseSchemaName = SchemaName;
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Projections.Snapshot<OrderAggregate>(SnapshotLifecycle.Inline);
            opts.Events.AggregateWriteCaching.Cache = theCache;
            opts.Events.CacheAggregatesForWriting<OrderAggregate>();
        });

        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public ValueTask DisposeAsync()
    {
        _store.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Guid> startOrder(string tenantId, OrderNumber number, string customer)
    {
        var streamId = Guid.NewGuid();
        await using var session = _store.LightweightSession(tenantId);
        session.Events.StartStream<OrderAggregate>(streamId, new OrderCreated(number, customer));
        await session.SaveChangesAsync();
        return streamId;
    }

    [Fact]
    public async Task the_same_key_names_a_stream_in_each_database()
    {
        var number = new OrderNumber("ORD-SHARED");
        var north = await startOrder("north", number, "Alice");
        var south = await startOrder("south", number, "Bob");

        await using var northSession = _store.LightweightSession("north");
        (await northSession.Events.FetchLatest<OrderAggregate, OrderNumber>(number))!.Id.ShouldBe(north);

        await using var southSession = _store.LightweightSession("south");
        (await southSession.Events.FetchLatest<OrderAggregate, OrderNumber>(number))!.Id.ShouldBe(south);
    }

    [Fact]
    public async Task a_cache_pointer_in_one_database_never_answers_for_another()
    {
        var number = new OrderNumber("ORD-CACHED");
        var north = await startOrder("north", number, "Alice");
        var south = await startOrder("south", number, "Bob");

        // Cached in north, with its key pointing at north's stream
        await using (var writing = _store.LightweightSession("north"))
        {
            var stream = await writing.Events.FetchForWriting<OrderAggregate, OrderNumber>(number);
            stream.AppendOne(new OrderItemAdded("Widget", 1m));
            await writing.SaveChangesAsync();
        }

        var hits = theCache.Hits;

        await using (var southSession = _store.LightweightSession("south"))
        {
            var stream = await southSession.Events.FetchForWriting<OrderAggregate, OrderNumber>(number);
            stream.Id.ShouldBe(south);
            stream.Aggregate!.CustomerName.ShouldBe("Bob");
            stream.CurrentVersion.ShouldBe(1);
        }

        theCache.Hits.ShouldBe(hits);

        await using var northSession = _store.LightweightSession("north");
        var cached = await northSession.Events.FetchForWriting<OrderAggregate, OrderNumber>(number);
        cached.Id.ShouldBe(north);
        cached.CurrentVersion.ShouldBe(2);
        theCache.Hits.ShouldBe(hits + 1);
    }
}
