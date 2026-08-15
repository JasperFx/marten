using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

/// <summary>
/// #5223: Count(predicate) over a child collection inside a Select() projection.
///
/// <para>
/// It already produced the right answer, by falling back to the client-side compiled transform
/// (GH-5011) — which means fetching and deserializing the whole document, collection included, to
/// return one integer. Where(x => x.Lines.Count(l => ...) > n) has been translated to
/// jsonb_array_length(jsonb_path_query_array(...)) since 9.14.1; these tests pin that the same
/// scalar is now available to a projection, and that anything untranslatable still falls back
/// rather than failing.
/// </para>
/// </summary>
public class Bug_5223_count_with_predicate_in_select: BugIntegrationContext
{
    private async Task<Guid> seedAsync()
    {
        var id = Guid.NewGuid();

        theSession.Store(new Order5223
        {
            Id = id,
            Name = "big",
            Lines =
            [
                new OrderLine5223 { Product = "foo", IsActive = true, Quantity = 2 },
                new OrderLine5223 { Product = "foo", IsActive = false, Quantity = 5 },
                new OrderLine5223 { Product = "bar", IsActive = true, Quantity = 1 },
                new OrderLine5223 { Product = "baz", IsActive = true, Quantity = 9 }
            ]
        });

        // A second document with an empty collection: the count must be 0, not null and not a row
        // that disappears from the result set.
        theSession.Store(new Order5223 { Id = Guid.NewGuid(), Name = "empty", Lines = [] });

        await theSession.SaveChangesAsync(TestContext.Current.CancellationToken);

        return id;
    }

    [Fact]
    public async Task count_with_a_predicate_is_computed_in_the_database()
    {
        var id = await seedAsync();

        var queryable = theSession.Query<Order5223>()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, ActiveCount = x.Lines.Count(line => line.IsActive), FooCount = x.Lines.Count(line => line.Product == "foo")
            });

        var sql = queryable.ToCommand().CommandText;

        sql.ShouldContain("jsonb_build_object");
        sql.ShouldContain("jsonb_array_length(jsonb_path_query_array(");

        var result = (await queryable.ToListAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        result.ActiveCount.ShouldBe(3);
        result.FooCount.ShouldBe(2);
    }

    [Fact]
    public async Task count_without_a_predicate_is_the_array_length()
    {
        var id = await seedAsync();

        var queryable = theSession.Query<Order5223>()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, Total = x.Lines.Count() });

        queryable.ToCommand().CommandText.ShouldContain("jsonb_array_length");

        (await queryable.ToListAsync(TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem().Total.ShouldBe(4);
    }

    [Fact]
    public async Task an_empty_collection_counts_zero()
    {
        await seedAsync();

        var results = await theSession.Query<Order5223>()
            .Select(x => new { x.Name, ActiveCount = x.Lines.Count(line => line.IsActive) })
            .ToListAsync(TestContext.Current.CancellationToken);

        results.Count.ShouldBe(2);
        results.Single(x => x.Name == "empty").ActiveCount.ShouldBe(0);
        results.Single(x => x.Name == "big").ActiveCount.ShouldBe(3);
    }

    [Fact]
    public async Task the_count_agrees_with_the_same_predicate_in_a_where_clause()
    {
        await seedAsync();

        // The Where() translation of this predicate shipped in 9.14.1. The projection must not
        // disagree with it -- same jsonpath filter, same elements counted.
        var viaWhere = await theSession.Query<Order5223>()
            .Where(x => x.Lines.Count(line => line.Quantity > 1) == 3)
            .CountAsync(TestContext.Current.CancellationToken);

        var viaSelect = await theSession.Query<Order5223>()
            .Select(x => new { Count = x.Lines.Count(line => line.Quantity > 1) })
            .ToListAsync(TestContext.Current.CancellationToken);

        viaWhere.ShouldBe(1);
        viaSelect.Count(x => x.Count == 3).ShouldBe(1);
    }

    [Fact]
    public async Task compound_predicates_are_translated()
    {
        var id = await seedAsync();

        var queryable = theSession.Query<Order5223>()
            .Where(x => x.Id == id)
            .Select(x => new { Count = x.Lines.Count(line => line.IsActive && line.Quantity > 1) });

        queryable.ToCommand().CommandText.ShouldContain("jsonb_path_query_array");

        (await queryable.ToListAsync(TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem().Count.ShouldBe(2);
    }

    [Fact]
    public async Task string_predicates_reach_the_jsonpath_tier_too()
    {
        var id = await seedAsync();

        var queryable = theSession.Query<Order5223>()
            .Where(x => x.Id == id)
            .Select(x => new { Count = x.Lines.Count(line => line.Product.StartsWith("b")) });

        queryable.ToCommand().CommandText.ShouldContain("starts with");

        (await queryable.ToListAsync(TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem().Count.ShouldBe(2);
    }

    [Fact]
    public async Task a_predicate_the_jsonpath_tier_cannot_express_still_returns_the_right_answer()
    {
        var id = await seedAsync();

        // An OR predicate is not translated: the jsonpath filter this tier emits joins its parts
        // with && only, so narrowing an || to a conjunction would silently undercount. It must fall
        // back to the client-side transform (GH-5011) rather than throwing or answering wrongly --
        // which is what Count() in a Select() did for EVERY predicate before #5223.
        var queryable = theSession.Query<Order5223>()
            .Where(x => x.Id == id)
            .Select(x => new { Count = x.Lines.Count(line => line.Product == "bar" || line.Quantity > 4) });

        queryable.ToCommand().CommandText.ShouldNotContain("jsonb_build_object");

        // qty 5 (foo), product bar, qty 9 (baz)
        (await queryable.ToListAsync(TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem().Count.ShouldBe(3);
    }

    [Fact]
    public async Task the_bare_boolean_predicate_from_the_issue_now_works_in_a_where_clause_too()
    {
        await seedAsync();

        // Same rewrite, reached through Where(). Before #5223 a bare boolean inside Count() was not
        // collection-aware, so this threw BadLinqExpressionException.
        var matches = await theSession.Query<Order5223>()
            .Where(x => x.Lines.Count(line => line.IsActive) == 3)
            .ToListAsync(TestContext.Current.CancellationToken);

        matches.ShouldHaveSingleItem().Name.ShouldBe("big");
    }

    [Fact]
    public async Task count_over_something_that_is_not_a_stored_collection_falls_back()
    {
        var id = await seedAsync();
        var local = new[] { 1, 2, 3 };

        var queryable = theSession.Query<Order5223>()
            .Where(x => x.Id == id)
            .Select(x => new { x.Name, Count = local.Count(i => i > 1) });

        (await queryable.ToListAsync(TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem().Count.ShouldBe(2);
    }
}

public class Order5223
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public List<OrderLine5223> Lines { get; set; } = new();
}

public class OrderLine5223
{
    public string Product { get; set; }
    public bool IsActive { get; set; }
    public int Quantity { get; set; }
}
