using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Schema.Indexing.FullText;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Indexes;

/// <summary>
/// #5298, ordering half. <c>ts_rank</c> orders results by relevance, resolving the SAME tsvector the
/// <c>Where</c> matched on — including a weighted one — so the rank and the filter cannot disagree.
///
/// <para>
/// The search term is bound as a parameter. The ngram precedent inlines its term with
/// <c>Replace("'", "''")</c>, which exists only because <c>OrderByFragment</c> held a
/// <c>List&lt;string&gt;</c> and had nowhere to put a parameter. It holds fragments now.
/// </para>
/// </summary>
[Collection("OneOffs")]
public class full_text_rank_ordering: OneOffConfigurationsContext
{
    public class Article
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
    }

    private async Task SeedWeightedStoreAsync()
    {
        StoreOptions(opts => opts.Schema.For<Article>().WeightedFullTextIndex(idx => idx
            .Weighted(a => a.Title, TextSearchWeight.A)
            .Weighted(a => a.Body, TextSearchWeight.D)));

        await using var session = theStore.LightweightSession();
        session.Store(
            // "kangaroo" in the BODY only -- weight D
            new Article { Id = Guid.NewGuid(), Title = "Something else entirely", Body = "a kangaroo appears here" },
            // "kangaroo" in the TITLE -- weight A, so this must rank first
            new Article { Id = Guid.NewGuid(), Title = "The kangaroo", Body = "nothing relevant in this body" });
        await session.SaveChangesAsync();
    }

    /// <summary>
    /// The whole point of weighting, end to end: a title match outranks a body match. Asserting on the
    /// generated SQL would not catch a rank computed over the wrong vector, which is the failure mode
    /// that matters here — so this asserts on the ORDER of real rows from PostgreSQL.
    /// </summary>
    [Fact]
    public async Task a_title_match_outranks_a_body_match()
    {
        await SeedWeightedStoreAsync();

        await using var query = theStore.QuerySession();
        var results = await query.Query<Article>()
            .Where(a => a.PlainTextSearch("kangaroo"))
            .OrderByTextRank("kangaroo", TextSearchFunction.Plain)
            .ToListAsync();

        results.Count.ShouldBe(2);
        results[0].Title.ShouldBe("The kangaroo");
    }

    /// <summary>
    /// The term is a parameter, not inlined. Checked on the command rather than by trusting the
    /// implementation, because this is the improvement over the ngram precedent that #5298 explicitly
    /// asked for.
    /// </summary>
    [Fact]
    public async Task the_search_term_is_parameterized()
    {
        await SeedWeightedStoreAsync();

        var command = theStore.QuerySession().Query<Article>()
            .Where(a => a.PlainTextSearch("kangaroo"))
            .OrderByTextRank("kangaroo", TextSearchFunction.Plain)
            .ToCommand();

        command.CommandText.ShouldContain("ts_rank(");
        command.CommandText.ShouldContain("order by");

        // The term appears only as a parameter value, never inlined into the SQL text.
        command.CommandText.ShouldNotContain("'kangaroo'");
        command.Parameters.Count.ShouldBe(2);
        command.Parameters.Any(p => (p.Value as string) == "kangaroo").ShouldBeTrue();
    }

    /// <summary>
    /// The rank has to resolve the same vector the filter did. With a weighted index that vector is a
    /// pre-built <c>setweight(...) || setweight(...)</c> expression rather than a
    /// <c>to_tsvector(config, text)</c> wrapper, so a rank that rebuilt the flat shape would rank over a
    /// different vector than it filtered on and be silently wrong.
    /// </summary>
    [Fact]
    public async Task the_rank_uses_the_same_vector_as_the_filter()
    {
        await SeedWeightedStoreAsync();

        var sql = theStore.QuerySession().Query<Article>()
            .Where(a => a.PlainTextSearch("kangaroo"))
            .OrderByTextRank("kangaroo", TextSearchFunction.Plain)
            .ToCommand().CommandText;

        var filterVector = sql.Split(" @@ ")[0].Split("where ")[1];
        var rankVector = sql.Split("ts_rank(")[1].Split(", plainto_tsquery")[0];

        rankVector.ShouldBe(filterVector);
        rankVector.ShouldContain("setweight");
    }

    /// <summary>
    /// Ranking must also work on an ordinary unweighted index, where the vector is the flat
    /// to_tsvector shape. Default ts_rank weights still separate a document mentioning the term twice
    /// from one mentioning it once.
    /// </summary>
    [Fact]
    public async Task ranking_works_over_an_unweighted_index()
    {
        StoreOptions(opts => opts.Schema.For<Article>().FullTextIndex(a => a.Title, a => a.Body));

        await using (var session = theStore.LightweightSession())
        {
            session.Store(
                new Article { Id = Guid.NewGuid(), Title = "one mention", Body = "wombat" },
                new Article { Id = Guid.NewGuid(), Title = "wombat wombat", Body = "wombat wombat wombat" });
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var results = await query.Query<Article>()
            .Where(a => a.PlainTextSearch("wombat"))
            .OrderByTextRank("wombat", TextSearchFunction.Plain)
            .ToListAsync();

        results.Count.ShouldBe(2);
        results[0].Title.ShouldBe("wombat wombat");
    }

    /// <summary>
    /// ThenByTextRank composes after a primary ordering rather than replacing it.
    /// </summary>
    [Fact]
    public async Task then_by_text_rank_composes_after_another_ordering()
    {
        await SeedWeightedStoreAsync();

        var sql = theStore.QuerySession().Query<Article>()
            .Where(a => a.PlainTextSearch("kangaroo"))
            .OrderBy(a => a.Title)
            .ThenByTextRank("kangaroo", TextSearchFunction.Plain)
            .ToCommand().CommandText;

        var orderBy = sql.Split("order by ")[1];
        orderBy.IndexOf("Title", StringComparison.Ordinal)
            .ShouldBeLessThan(orderBy.IndexOf("ts_rank", StringComparison.Ordinal));
    }
}
