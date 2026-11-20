#pragma warning disable MARTEN001 // cold event storage is experimental
using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Cold.Matrix;
using EventSourcingTests.FetchForWriting;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Cold;
using Marten.Events.Schema;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Cold;

/// <summary>
///     <c>StreamState.ColdVersion</c> (jasperfx#1211), the partial index on <c>cold_version</c>, compacting before
///     moving cold, and the write cache across a cold move.
/// </summary>
public class cold_boundary_in_stream_state_and_schema: OneOffConfigurationsContext
{
    private readonly InMemoryColdEventStore _cold = new();

    private void configure(Action<StoreOptions>? more = null)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            more?.Invoke(opts);
        });
    }

    private async Task<string> startAsync<T>(params object[] events) where T : class
    {
        var key = Guid.NewGuid().ToString();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<T>(key, events);
        await session.SaveChangesAsync();
        return key;
    }

    private static object[] cartEvents(string key) =>
    [
        new MatrixOpened("anne", new MatrixOrderNumber(key)), new MatrixItemAdded("a"), new MatrixItemAdded("b"),
        new MatrixItemAdded("c")
    ];

    [Fact]
    public async Task stream_state_carries_the_cold_version()
    {
        configure();
        var cold = await startAsync<LiveKeyedCart>(cartEvents("1"));
        var warm = await startAsync<LiveKeyedCart>(cartEvents("2"));
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, cold, 2);

        await using var query = theStore.QuerySession();

        var state = (await query.Events.FetchStreamStateAsync(cold)).ShouldNotBeNull();
        state.ColdVersion.ShouldBe(2);
        state.Version.ShouldBe(4);
        (await query.Events.FetchStreamStateAsync(warm)).ShouldNotBeNull().ColdVersion.ShouldBeNull();

        // Through JasperFx's read-only store too, as CritterWatch reads it
        var readOnly = (IReadOnlyEventStore)query.Events;
        (await readOnly.FetchStreamStateAsync(cold)).ShouldNotBeNull().ColdVersion.ShouldBe(2);

        var batch = query.CreateBatchQuery();
        var batched = batch.Events.FetchStreamState(cold);
        await batch.Execute();
        (await batched).ColdVersion.ShouldBe(2);
    }

    [Fact]
    public async Task stream_states_can_be_queried_by_their_cold_version()
    {
        configure();
        var cold = await startAsync<LiveKeyedCart>(cartEvents("1"));
        var warm = await startAsync<LiveKeyedCart>(cartEvents("2"));
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, cold, 3);

        await using var query = theStore.QuerySession();

        var states = query.Events.QueryStreamStates().Where(x => x.ColdVersion != null).ToList();
        states.ShouldHaveSingleItem().Key.ShouldBe(cold);
        states[0].ColdVersion.ShouldBe(3);
        states[0].TenantId.ShouldBe(StorageConstants.DefaultTenantId);

        query.Events.QueryStreamStates().Where(x => x.ColdVersion == null).Select(x => x.Key).ToList()
            .ShouldBe([warm]);
        query.Events.QueryStreamStates().Where(x => x.ColdVersion > 2).Count().ShouldBe(1);
    }

    [Fact]
    public async Task without_a_cold_store_the_cold_version_is_always_null()
    {
        StoreOptions(opts => opts.Events.StreamIdentity = StreamIdentity.AsString);
        var key = await startAsync<LiveKeyedCart>(cartEvents("1"));

        await using var query = theStore.QuerySession();
        (await query.Events.FetchStreamStateAsync(key)).ShouldNotBeNull().ColdVersion.ShouldBeNull();
        query.Events.QueryStreamStates().Where(x => x.ColdVersion == null).Count().ShouldBe(1);
        query.Events.QueryStreamStates().Where(x => x.ColdVersion != null).Count().ShouldBe(0);
    }

    [Fact]
    public async Task only_cold_streams_are_in_the_index_on_the_cold_version()
    {
        configure();
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var definition = (string?)await conn
            .CreateCommand("select indexdef from pg_indexes where schemaname = :schema and indexname = :name")
            .With("schema", SchemaName)
            .With("name", StreamsTable.ColdVersionIndexName)
            .ExecuteScalarAsync();

        definition.ShouldNotBeNull().ShouldContain("WHERE (cold_version IS NOT NULL)");

        // And the schema is stable: applying it again changes nothing
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task without_a_cold_store_there_is_no_cold_version_and_no_index()
    {
        StoreOptions(opts => opts.Events.StreamIdentity = StreamIdentity.AsString);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var columns = (long)(await conn
            .CreateCommand(
                "select count(*) from information_schema.columns where table_schema = :schema and table_name = 'mt_streams' and column_name = 'cold_version'")
            .With("schema", SchemaName)
            .ExecuteScalarAsync())!;
        columns.ShouldBe(0);

        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task compacting_first_and_then_moving_cold_works()
    {
        // Compaction leaves a Compacted<T> at the compacted version; moving the stream cold through it moves that
        // row too, and the cold part then starts with a gap the reader accepts
        configure();
        var key = await startAsync<LiveKeyedCart>(cartEvents("1"));

        await using (var session = theStore.LightweightSession())
        {
            await session.Events.CompactStreamAsync<LiveKeyedCart>(key, x => x.Version = 3);
            await session.SaveChangesAsync();
        }

        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, 3);
        _cold.StoredVersions(StorageConstants.DefaultTenantId, key).ShouldBe([3L]);

        await using (var session = theStore.LightweightSession())
        {
            ColdMatrixRun.ShouldBeTheWholeStream(await session.Events.AggregateStreamAsync<LiveKeyedCart>(key));
            ColdMatrixRun.ShouldBeTheWholeStream(await session.Events.FetchLatest<LiveKeyedCart>(key));

            var stream = await session.Events.FetchForWriting<LiveKeyedCart>(key);
            ColdMatrixRun.ShouldBeTheWholeStream(stream.Aggregate);
            stream.AppendOne(new MatrixItemAdded("d"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            ColdMatrixRun.ShouldBeTheWholeStream(await session.Events.FetchLatest<LiveKeyedCart>(key), "d");
        }
    }
}

/// <summary>
///     §4.1 of the cold storage study: an aggregate cached for writing at version V, and a move through C &gt; V on
///     another node. The events V+1..C are no longer in mt_events, so folding the warm events onto the cached
///     aggregate would skip them without an error. A cold boundary past the cached version is a cache miss.
/// </summary>
public class the_write_cache_across_a_cold_move: OneOffConfigurationsContext
{
    private readonly InMemoryColdEventStore _cold = new();
    private readonly MartenTestAggregateWriteCache _cache = new();

    private void configure(ProjectionLifecycle lifecycle)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseColdEventStore(_cold);
            opts.Events.AggregateWriteCaching.Cache = _cache;
            opts.Events.CacheAggregatesForWriting<LiveKeyedCachedCart>();
            opts.Events.CacheAggregatesForWriting<AsyncKeyedCachedCart>();
            if (lifecycle == ProjectionLifecycle.Async)
            {
                opts.Projections.Snapshot<AsyncKeyedCachedCart>(SnapshotLifecycle.Async);
            }
        });
    }

    private async Task<string> staleCacheAsync<T>(long coldThrough, bool async) where T : MatrixCart
    {
        var key = Guid.NewGuid().ToString();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<T>(key, new MatrixOpened("anne", new MatrixOrderNumber(key)),
                new MatrixItemAdded("a"));
            await session.SaveChangesAsync();
        }

        await catchUpAsync(async);

        // Cached at version 2
        await using (var session = theStore.LightweightSession())
        {
            await session.Events.FetchForWriting<T>(key);
            await session.SaveChangesAsync();
        }

        _cache.SingleEntry().Version.ShouldBe(2);

        // Another node appends past the cached version and moves the stream cold, never touching this cache
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(key, new MatrixItemAdded("b"), new MatrixItemAdded("c"));
            await session.SaveChangesAsync();
        }

        await catchUpAsync(async);
        await theStore.MoveToColdAsync(_cold, StorageConstants.DefaultTenantId, key, coldThrough);
        return key;
    }

    private async Task catchUpAsync(bool async)
    {
        if (!async) return;

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await theStore.Storage.Database.WaitForNonStaleProjectionDataAsync(TimeSpan.FromSeconds(60));
        await daemon.StopAllAsync();
    }

    public static TheoryData<ProjectionLifecycle, bool, long> Cases => new()
    {
        // Past the cached version: a miss. At or below it: the cached aggregate is still the start.
        { ProjectionLifecycle.Live, false, 3 }, { ProjectionLifecycle.Live, true, 3 },
        { ProjectionLifecycle.Live, false, 4 }, { ProjectionLifecycle.Live, true, 4 },
        { ProjectionLifecycle.Live, false, 2 }, { ProjectionLifecycle.Live, true, 2 },
        { ProjectionLifecycle.Async, false, 3 }, { ProjectionLifecycle.Async, true, 3 },
        { ProjectionLifecycle.Async, false, 4 }, { ProjectionLifecycle.Async, true, 4 },
        { ProjectionLifecycle.Async, false, 2 }, { ProjectionLifecycle.Async, true, 2 }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public Task the_fetch_folds_the_whole_stream(ProjectionLifecycle lifecycle, bool batched, long coldThrough)
    {
        configure(lifecycle);
        return lifecycle == ProjectionLifecycle.Async
            ? runAsync<AsyncKeyedCachedCart>(batched, coldThrough, true)
            : runAsync<LiveKeyedCachedCart>(batched, coldThrough, false);
    }

    private async Task runAsync<T>(bool batched, long coldThrough, bool async) where T : MatrixCart
    {
        var key = await staleCacheAsync<T>(coldThrough, async);
        var hits = _cache.Hits;

        await using var session = theStore.LightweightSession();
        IEventStream<T> stream;
        if (batched)
        {
            var batch = session.CreateBatchQuery();
            var pending = batch.Events.FetchForWriting<T>(key);
            await batch.Execute();
            stream = await pending;
        }
        else
        {
            stream = await session.Events.FetchForWriting<T>(key);
        }

        // The cached aggregate was taken either way
        _cache.Hits.ShouldBe(hits + 1);

        ColdMatrixRun.ShouldBeTheWholeStream(stream.Aggregate);
        stream.StartingVersion.ShouldBe(4);

        stream.AppendOne(new MatrixItemAdded("d"));
        await session.SaveChangesAsync();

        // Written back after the commit, at a version that holds the whole stream
        await using var next = theStore.LightweightSession();
        ColdMatrixRun.ShouldBeTheWholeStream((await next.Events.FetchForWriting<T>(key)).Aggregate, "d");
    }
}
