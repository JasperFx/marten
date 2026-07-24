namespace Marten.Linq;

/// <summary>
///     Per-query opt-in for the <see cref="QueryPlanCache" />. Named distinctly from
///     <see cref="QueryPlan" /> (the Postgres EXPLAIN model) to avoid confusion between the
///     two unrelated concepts.
/// </summary>
public enum QueryPlanCaching
{
    /// <summary>
    ///     Parse and compile this query normally. The default.
    /// </summary>
    Default = 0,

    /// <summary>
    ///     Attempt to reuse a cached compiled plan for this query's structural shape via
    ///     <c>StoreOptions.Linq.QueryPlanCache</c>. Has no effect unless the store has an
    ///     enabled <see cref="QueryPlanCache" /> (see <see cref="QueryPlanCache.PerShape" />),
    ///     and only applies to shapes the cache knows how to safely replay -- anything else
    ///     silently falls back to the normal, uncached execution path.
    /// </summary>
    Cached = 1
}
