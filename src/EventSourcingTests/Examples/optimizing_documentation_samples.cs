#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using JasperFx.Events.Fetching;
using JasperFx.Events.Projections;
using Marten;

namespace EventSourcingTests.Examples.OptimizingSamples;

public class Order
{
    public Guid Id { get; set; }
}

public class MyAggregate
{
    public Guid Id { get; set; }
}

/// <summary>
///     Just enough of an <see cref="IAggregateWriteCache" /> for the sample to compile
/// </summary>
public class MyAggregateWriteCache: IAggregateWriteCache
{
    private readonly ConcurrentDictionary<AggregateCacheKey, (object Aggregate, long Version)> _entries = new();

    public bool TryTake(AggregateCacheKey key, [NotNullWhen(true)] out object? aggregate, out long version)
    {
        if (_entries.TryRemove(key, out var entry))
        {
            aggregate = entry.Aggregate;
            version = entry.Version;
            return true;
        }

        aggregate = null;
        version = 0;
        return false;
    }

    public void Store(AggregateCacheKey key, object aggregate, long version) => _entries[key] = (aggregate, version);

    public void Evict(AggregateCacheKey key) => _entries.TryRemove(key, out _);
}

/// <summary>
///     The code samples in docs/events/optimizing.md that no other test shows. Compiled only, never run.
/// </summary>
public static class optimizing_documentation_samples
{
    public static void cache_snapshots_for_writing(StoreOptions opts)
    {
        #region sample_optimizing_cache_aggregates_for_writing

        opts.Projections.Snapshot<Order>(SnapshotLifecycle.Async);

        // Keep up to 1000 recently fetched Order snapshots
        opts.Events.CacheAggregatesForWriting<Order>(sizeLimit: 1000);

        #endregion
    }

    public static void cache_live_aggregates_for_writing(StoreOptions opts)
    {
        #region sample_optimizing_cache_live_aggregates_for_writing

        // No Snapshot<Order>() registration: Order is built Live
        opts.Events.CacheAggregatesForWriting<Order>();

        #endregion
    }

    public static void supply_your_own_cache(StoreOptions opts)
    {
        #region sample_optimizing_custom_aggregate_write_cache

        opts.Events.AggregateWriteCaching.Cache = new MyAggregateWriteCache();
        opts.Events.CacheAggregatesForWriting<Order>();

        #endregion
    }

    public static void smaller_daemon_batches(StoreOptions opts)
    {
        #region sample_optimizing_smaller_batch_size

        opts.Projections.Snapshot<MyAggregate>(SnapshotLifecycle.Async, asyncOptions =>
        {
            asyncOptions.BatchSize = 100;
        });

        #endregion
    }
}
