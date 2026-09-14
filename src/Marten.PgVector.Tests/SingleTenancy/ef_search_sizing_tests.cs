using System;
using System.Linq;
using Marten.PgVector;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector.Tests.SingleTenancy;

/// <summary>
///     #5419: how many candidates the HNSW scan is told to consider, decided without a database.
/// </summary>
/// <remarks>
///     <para>
///         <b>These are unit tests on purpose, and the reason is a mistake worth recording.</b> The
///         obvious way to cover the ceiling and the no-index case is end to end — declare an index,
///         search with a huge limit, count the rows. I wrote those, and <b>both passed against the
///         unfixed code</b>: at 2500 rows the planner chooses a <c>Seq Scan</c> for a limit of 2000 and
///         for a filtered search, so neither query ever reached the HNSW path the fix is about. An
///         exact scan returns everything asked for whether or not <c>hnsw.ef_search</c> was set.
///     </para>
///     <para>
///         ⚠️ <b>An end-to-end vector test is only meaningful if the plan is pinned</b>, which is why
///         the one genuine integration fact for this lives in <c>vector_index_tests</c> beside
///         <c>the_planner_uses_the_index_for_the_search_statement</c>, and why the rest is asserted
///         here against the decision itself rather than against the planner's mood.
///     </para>
/// </remarks>
public class ef_search_sizing_tests
{
    public class Indexed
    {
        public Guid Id { get; set; }
        public float[]? Embedding { get; set; }
    }

    public class NotIndexed
    {
        public Guid Id { get; set; }
        public float[]? Embedding { get; set; }
    }

    private static StoreOptions options()
    {
        var opts = new StoreOptions();
        opts.Connection(ConnectionSource.ConnectionString);
        opts.UsePgVector();
        opts.VectorIndex<Indexed>(x => x.Embedding, 3);
        opts.RegisterDocumentType<NotIndexed>();
        return opts;
    }

    private static int? resolve<T>(int rowsNeeded)
    {
        var member = typeof(T).GetProperty("Embedding")!;
        return VectorSearchRunner.ResolveEfSearch<T>(options(), member, rowsNeeded);
    }

    /// <summary>
    ///     ⚠️ The ceiling is pgvector's, not a preference: it validates <c>hnsw.ef_search</c> against
    ///     1..1000 and ERRORS outside it. Passing a raw limit through would turn a search for 2000 rows
    ///     from a silent truncation into an exception, which is a different bug rather than a fix.
    /// </summary>
    [Fact]
    public void a_request_past_pgvectors_ceiling_is_clamped_to_it()
    {
        resolve<Indexed>(2000).ShouldBe(1000);
        resolve<Indexed>(1001).ShouldBe(1000);
        resolve<Indexed>(1000).ShouldBe(1000);
    }

    /// <summary>
    ///     A small limit never LOWERS recall below what pgvector does by default.
    /// </summary>
    /// <remarks>
    ///     Sizing ef_search to the request alone would set it to 3 for a three-row search, which
    ///     searches fewer candidates than doing nothing at all — a fix that made small searches worse.
    /// </remarks>
    [Fact]
    public void a_small_request_does_not_drop_below_pgvectors_default()
    {
        resolve<Indexed>(1).ShouldBe(40);
        resolve<Indexed>(39).ShouldBe(40);
        resolve<Indexed>(40).ShouldBe(40);
        resolve<Indexed>(41).ShouldBe(41);
    }

    /// <summary>
    ///     No vector index means no HNSW scan to tune, and null is what keeps the search off the
    ///     transaction path entirely.
    /// </summary>
    /// <remarks>
    ///     Marten's searches work without an index — it makes them fast rather than possible — and an
    ///     exact scan already returns the full limit. Setting the GUC there would cost a transaction and
    ///     two round trips to change nothing.
    /// </remarks>
    [Fact]
    public void a_member_with_no_index_is_not_tuned_at_all()
    {
        resolve<NotIndexed>(100).ShouldBeNull();
    }
}
