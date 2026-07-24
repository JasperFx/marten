#nullable enable
using System;
using System.Collections.Concurrent;
using Marten.Linq.Caching;

namespace Marten.Linq;

/// <summary>
///     Opt-in, bounded cache that reuses compiled LINQ query plans across calls that share
///     the same structural "shape" (the same Where/OrderBy/ThenBy/Skip/Take/Select chain)
///     but differ only in the captured filter values -- e.g. endpoints with optional /
///     conditional <c>Where()</c> clauses that can't otherwise use <c>ICompiledQuery</c>
///     because every filter combination produces a different shape.
/// </summary>
/// <remarks>
///     See https://github.com/JasperFx/marten/issues/5013. Disabled by default; enable with
///     <c>storeOptions.Linq.QueryPlanCache = QueryPlanCache.PerShape();</c> or
///     <c>storeOptions.Linq.EnableQueryPlanCaching();</c>, then opt individual queries in
///     with <c>query.ToListAsync(token, QueryPlanCaching.Cached)</c>.
/// </remarks>
public sealed class QueryPlanCache
{
    private readonly ConcurrentDictionary<string, CachedLinqPlan> _entries = new();
    private readonly ConcurrentQueue<string> _insertionOrder = new();

    private QueryPlanCache(int maxEntries, bool enabled)
    {
        MaxEntries = maxEntries;
        Enabled = enabled;
    }

    /// <summary>
    ///     The maximum number of distinct shapes that will be cached. Once exceeded, the
    ///     oldest cached shapes are evicted first.
    /// </summary>
    public int MaxEntries { get; }

    /// <summary>
    ///     Whether this cache is active. The default <see cref="Disabled" /> instance always
    ///     returns <c>false</c> here.
    /// </summary>
    public bool Enabled { get; }

    /// <summary>
    ///     The number of shapes currently cached.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    ///     The default, disabled cache. Query plan caching is opt-in.
    /// </summary>
    public static QueryPlanCache Disabled { get; } = new(0, false);

    /// <summary>
    ///     Creates an enabled, bounded per-shape query plan cache.
    /// </summary>
    /// <param name="maxEntries">The maximum number of distinct query shapes to cache.</param>
    public static QueryPlanCache PerShape(int maxEntries = 1024)
    {
        if (maxEntries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "Must be greater than zero");
        }

        return new QueryPlanCache(maxEntries, true);
    }

    internal bool TryGet(string key, out CachedLinqPlan plan)
    {
        return _entries.TryGetValue(key, out plan!);
    }

    internal void Set(string key, CachedLinqPlan plan)
    {
        if (!_entries.TryAdd(key, plan))
        {
            return;
        }

        _insertionOrder.Enqueue(key);
        trimIfNecessary();
    }

    private void trimIfNecessary()
    {
        while (_entries.Count > MaxEntries && _insertionOrder.TryDequeue(out var oldest))
        {
            _entries.TryRemove(oldest, out _);
        }
    }

    /// <summary>
    ///     Removes every cached plan. Mostly useful for tests.
    /// </summary>
    public void Clear()
    {
        _entries.Clear();
        while (_insertionOrder.TryDequeue(out _))
        {
        }
    }
}

