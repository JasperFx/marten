using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Vectors;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.PgVector.Tests.SingleTenancy;

/// <summary>
///     Hybrid search — reciprocal rank fusion over the <c>ts_rank</c> leg and the vector leg.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Almost every assertion about a hybrid search is satisfied by either leg alone</b>, so
///         the corpus here is built so the legs DISAGREE: an exact keyword match whose embedding points
///         away from the query, a paraphrase whose embedding is exact but which shares no tokens, and a
///         middling-in-both document that only the fusion can lift above them.
///     </para>
/// </remarks>
[Collection("Marten.PgVector")]
public class hybrid_search_tests : IAsyncLifetime
{
    private DocumentStore _store = null!;

    // The query vector. "Keyword" points away from it, "Semantic" is exactly it, "Both" is between.
    private static ReadOnlyMemory<float> Query => new[] { 1.0f, 0.0f, 0.0f };

    public async ValueTask InitializeAsync()
    {
        #region sample_pgvector_hybrid_search_setup
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_hybrid_tests";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.UsePgVector();

            // The text leg searches through this index. Without one it still works,
            // but over the whole document and without an index.
            opts.Schema.For<Article>().FullTextIndex();
        });
        #endregion

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var session = _store.LightweightSession();
        session.Store(
            // Wins the text leg outright, loses the vector leg outright.
            new Article
            {
                Id = Guid.NewGuid(), Name = "Keyword",
                Body = "quantum quantum quantum widget catalogue",
                Embedding = [0.0f, 0.0f, 1.0f]
            },
            // Wins the vector leg outright, shares no tokens with the query at all.
            new Article
            {
                Id = Guid.NewGuid(), Name = "Semantic",
                Body = "a device for measuring subatomic fluctuations",
                Embedding = [1.0f, 0.0f, 0.0f]
            },
            // Second in both. Only the fusion puts it first.
            new Article
            {
                Id = Guid.NewGuid(), Name = "Both",
                Body = "quantum sensor",
                Embedding = [0.8f, 0.6f, 0.0f]
            });

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    /// <summary>
    ///     ⚠️ The discriminating test. Each leg has a different outright winner, and the document that
    ///     is merely second in both beats them once the ranks are fused — because it is the only one
    ///     both legs agree on.
    /// </summary>
    /// <remarks>
    ///     With <c>k = 60</c> and a candidate depth of two: the single-leg winners score
    ///     <c>1/61 ≈ 0.0164</c> each, and the agreed document scores <c>1/62 + 1/62 ≈ 0.0323</c>.
    ///     Delete either leg from the fusion and this fails.
    /// </remarks>
    [Fact]
    public async Task agreement_between_the_legs_outranks_a_single_leg_winner()
    {
        await using var query = _store.QuerySession();

        #region sample_pgvector_hybrid_search
        // The search text feeds the full-text leg, the query vector feeds the vector leg,
        // and the two rankings are fused with reciprocal rank fusion
        var results = await query.HybridSearchAsync<Article>(
            x => x.Embedding, "quantum", Query,
            limit: 2,
            options: new HybridSearchOptions(CandidateDepth: 2));
        #endregion

        results[0].Name.ShouldBe("Both");
    }

    /// <summary>
    ///     The fusion is over the UNION, so a document only one leg found still scores. That is the
    ///     point rather than a tolerance — what the keyword leg alone finds is what the vector leg is
    ///     bad at.
    /// </summary>
    [Fact]
    public async Task a_document_only_one_leg_found_still_scores()
    {
        await using var query = _store.QuerySession();

        var results = await query.HybridSearchAsync<Article>(x => x.Embedding, "quantum", Query);

        // "Semantic" shares no tokens with "quantum" at all, so only the vector leg found it.
        results.Select(x => x.Name).ShouldContain("Semantic");
        results.Count.ShouldBe(3);
    }

    /// <summary>
    ///     Larger is better, and the scores are a strictly descending sequence — the ordering and the
    ///     number cannot disagree.
    /// </summary>
    [Fact]
    public async Task scores_come_back_descending()
    {
        await using var query = _store.QuerySession();

        #region sample_pgvector_hybrid_search_with_scores
        // Each HybridMatch<T> carries the document and its fused score. Larger is better.
        var scored = await query.HybridSearchWithScoresAsync<Article>(x => x.Embedding, "quantum", Query);
        #endregion

        scored.Select(x => x.Score).ShouldBe(scored.Select(x => x.Score).OrderByDescending(s => s));
        scored.First().Score.ShouldBeGreaterThan(0);
    }

    /// <summary>
    ///     Web style is the other safe way to hand a search box's contents straight through — here a
    ///     leading '-' excludes the document that would otherwise win the text leg.
    /// </summary>
    [Fact]
    public async Task web_style_reaches_the_other_tsquery_function()
    {
        await using var query = _store.QuerySession();

        var results = await query.HybridSearchAsync<Article>(
            x => x.Embedding, "quantum -catalogue", Query,
            limit: 5,
            options: new HybridSearchOptions(TextStyle: HybridTextStyle.WebStyle, CandidateDepth: 5));

        // The text leg can no longer see "Keyword", so it is left with whatever the vector leg gave it.
        var names = results.Select(x => x.Name).ToList();
        names.ShouldContain("Both");
        names.IndexOf("Keyword").ShouldBeGreaterThan(names.IndexOf("Both"));
    }

    /// <summary>
    ///     A candidate depth below the limit would fuse fewer candidates than it is asked to return,
    ///     which is the opposite of what the knob is for. Refused by name.
    /// </summary>
    [Fact]
    public async Task a_candidate_depth_below_the_limit_is_refused()
    {
        await using var query = _store.QuerySession();

        var ex = await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await query.HybridSearchAsync<Article>(
                x => x.Embedding, "quantum", Query, limit: 10,
                options: new HybridSearchOptions(CandidateDepth: 2)));

        ex.Message.ShouldContain("read DEEPER than limit");
    }

    /// <summary>
    ///     k is the smoothing constant, and zero would make a first-placed document score infinitely.
    /// </summary>
    [Fact]
    public async Task a_zero_smoothing_constant_is_refused()
    {
        await using var query = _store.QuerySession();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await query.HybridSearchAsync<Article>(
                x => x.Embedding, "quantum", Query, options: new HybridSearchOptions(K: 0)));
    }

    /// <summary>
    ///     ⚠️ #5446 / jasperfx#854: <c>ColumnWeights</c> is REFUSED here rather than ignored.
    /// </summary>
    /// <remarks>
    ///     Marten weights full-text columns at INDEX time through <c>WeightedFullTextIndex</c>, so
    ///     there is nothing a per-call weight could be applied to. Ignoring it is the dangerous
    ///     option: a caller who weighted their title column and silently got an unweighted ranking has
    ///     no way to find out, because the search still returns plausible documents in a plausible
    ///     order — the same failure shape as the <c>Distance</c> default the shared options record was
    ///     created to fix.
    /// </remarks>
    [Fact]
    public async Task column_weights_are_refused_by_name_rather_than_ignored()
    {
        await using var query = _store.QuerySession();

        var ex = await Should.ThrowAsync<NotSupportedException>(async () =>
            await query.HybridSearchAsync<Article>(
                x => x.Embedding, "quantum", Query,
                options: new HybridSearchOptions(ColumnWeights: [3.0, 1.0])));

        ex.Message.ShouldContain("Marten");
        ex.Message.ShouldContain("ColumnWeights");
        ex.Message.ShouldContain("WeightedFullTextIndex");
    }

    /// <summary>The default is untouched — only a NON-NULL value is refused.</summary>
    [Fact]
    public async Task the_default_of_no_column_weights_is_unaffected()
    {
        await using var query = _store.QuerySession();

        var results = await query.HybridSearchAsync<Article>(
            x => x.Embedding, "quantum", Query, options: new HybridSearchOptions(K: 30));

        results.ShouldNotBeEmpty();
    }
}

public class Article
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Body { get; set; } = "";
    public float[]? Embedding { get; set; }
}
