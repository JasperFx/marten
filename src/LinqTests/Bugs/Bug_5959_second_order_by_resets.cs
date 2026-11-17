using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class ReorderedDoc
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int Score { get; set; }
    public List<ReorderedChild> Children { get; set; } = [];
}

public record ReorderedChild
{
    public string Label { get; set; } = "";
    public int Rank { get; set; }
}

public static class ReorderedProbe
{
    public static int Value = 3;
}

// #5959: a second OrderBy() re-sorts the whole sequence in LINQ to Objects, and the sort is stable, so the earlier
// ordering only breaks the ties the later one leaves: OrderBy(a).OrderBy(b) orders by b, then a. Marten put the
// later key behind the earlier one, `order by a, b`. Every chain here is compared with LINQ to Objects, in order.
// Each chain's FIRST ordering ends in a unique key, so LINQ to Objects' order is fully determined.
public class Bug_5959_second_order_by_resets: IntegrationContext
{
    private static readonly ReorderedDoc[] Docs = Enumerable.Range(1, 8).Select(i => new ReorderedDoc
    {
        Id = Guid.NewGuid(),
        Number = i,
        Name = new[] { "h", "c", "f", "a", "g", "d", "b", "e" }[i - 1],
        Category = i % 2 == 0 ? "x" : "y",
        Score = i % 3,
        Children = [new ReorderedChild { Label = "l" + (9 - i), Rank = i % 2 }, new ReorderedChild { Label = "k" + i, Rank = i % 3 }]
    }).ToArray();

    public Bug_5959_second_order_by_resets(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(ReorderedDoc)];

    protected override async Task fixtureSetup()
    {
        // One at a time, out of Number order, so an ordering that goes missing shows up
        foreach (var doc in Docs.OrderBy(x => x.Name))
        {
            theSession.Store(doc);
            await theSession.SaveChangesAsync();
        }
    }

    public static IEnumerable<object[]> ChainNames() => ReorderingChains.All().Keys.Select(x => new object[] { x });

    [Theory]
    [MemberData(nameof(ChainNames))]
    public async Task chain_matches_linq_to_objects(string name)
    {
        var chain = ReorderingChains.All()[name];
        var expected = chain(Docs.AsQueryable()).Select(x => x.Number).ToList();

        await using var session = theStore.QuerySession();
        var query = chain(session.Query<ReorderedDoc>()).Select(x => x.Number);
        var actual = await query.ToListAsync();

        actual.ShouldBe(expected, query.ToCommand().CommandText);
    }

    [Fact]
    public async Task a_single_chain_keeps_its_sql()
    {
        // OrderBy().ThenBy() with no second OrderBy() is the shape every existing query has
        await using var session = theStore.QuerySession();

        session.Query<ReorderedDoc>().OrderBy(x => x.Category).ThenByDescending(x => x.Score).ThenBy(x => x.Name)
            .ToCommand().CommandText.ShouldContain(
                "order by d.data ->> 'Category', CAST(d.data ->> 'Score' as integer) desc, d.data ->> 'Name'");
    }

    [Fact]
    public async Task the_later_order_by_leads_the_sql()
    {
        await using var session = theStore.QuerySession();

        session.Query<ReorderedDoc>().OrderBy(x => x.Category).OrderBy(x => x.Name).ToCommand().CommandText
            .ShouldContain("order by d.data ->> 'Name', d.data ->> 'Category'");
    }

    [Fact]
    public async Task first_after_a_second_order_by()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => x.Number).OrderByDescending(x => x.Score).First();
        var actual = await session.Query<ReorderedDoc>().OrderBy(x => x.Number).OrderByDescending(x => x.Score)
            .FirstAsync();

        actual.Number.ShouldBe(expected.Number);
    }

    [Fact]
    public async Task order_by_sql_starts_an_ordering_too()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => x.Number).OrderBy(x => x.Category).Select(x => x.Number).ToList();
        var actual = await session.Query<ReorderedDoc>().OrderBy(x => x.Number).OrderBySql("d.data ->> 'Category'")
            .Select(x => x.Number).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task then_by_sql_extends_the_later_ordering()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => x.Number).OrderBy(x => x.Category).ThenBy(x => x.Score)
            .Select(x => x.Number).ToList();
        var actual = await session.Query<ReorderedDoc>().OrderBy(x => x.Number).OrderBy(x => x.Category)
            .ThenBySql("CAST(d.data ->> 'Score' as integer)").Select(x => x.Number).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task order_by_several_property_names_keeps_them_in_order()
    {
        // OrderBy("a", "b") means a, then b. It used to be built as OrderBy(a).OrderBy(b), which only meant that
        // while Marten got the second OrderBy() wrong; it is now built as OrderBy(a).ThenBy(b)
        await using var session = theStore.QuerySession();

        var query = session.Query<ReorderedDoc>().OrderBy("Category", "Name desc");
        query.ToCommand().CommandText.ShouldBe(session.Query<ReorderedDoc>().OrderBy(x => x.Category)
            .ThenByDescending(x => x.Name).ToCommand().CommandText);

        var expected = Docs.OrderBy(x => x.Category).ThenByDescending(x => x.Name).Select(x => x.Number).ToList();
        (await query.Select(x => x.Number).ToListAsync()).ShouldBe(expected);

        var batch = session.CreateBatchQuery();
        var batched = batch.Query<ReorderedDoc>().OrderBy("Category", "Name desc").ToList();
        await batch.Execute();
        (await batched).Select(x => x.Number).ToList().ShouldBe(expected);
    }

    [Fact]
    public async Task second_order_by_over_select_many_children()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.SelectMany(x => x.Children).OrderBy(x => x.Label).OrderBy(x => x.Rank)
            .Select(x => x.Label).ToList();
        var actual = await session.Query<ReorderedDoc>().SelectMany(x => x.Children).OrderBy(x => x.Label)
            .OrderBy(x => x.Rank).Select(x => x.Label).ToListAsync();

        actual.ShouldBe(expected);
    }
}

internal static class ReorderingChains
{
    public static Dictionary<string, Func<IQueryable<ReorderedDoc>, IQueryable<ReorderedDoc>>> All()
    {
        var local = 5;

        return new()
        {
            ["order_by_then_order_by"] = q => q.OrderBy(x => x.Name).OrderBy(x => x.Category),
            ["order_by_then_order_by_descending"] = q => q.OrderBy(x => x.Number).OrderByDescending(x => x.Score),
            ["descending_then_order_by"] = q => q.OrderByDescending(x => x.Name).OrderBy(x => x.Category),
            ["three_order_bys"] = q => q.OrderBy(x => x.Number).OrderBy(x => x.Category).OrderByDescending(x => x.Score),
            ["chain_then_order_by"] = q => q.OrderBy(x => x.Category).ThenBy(x => x.Name).OrderBy(x => x.Score),
            ["order_by_then_chain"] = q =>
                q.OrderByDescending(x => x.Number).OrderBy(x => x.Score).ThenByDescending(x => x.Category),
            ["chain_then_chain"] = q => q.OrderBy(x => x.Score).ThenByDescending(x => x.Number)
                .OrderByDescending(x => x.Category).ThenBy(x => x.Score),
            ["same_key_twice"] = q => q.OrderBy(x => x.Name).OrderByDescending(x => x.Name),
            ["closed_order_by_after_a_key"] = q => q.OrderBy(x => x.Name).OrderBy(x => ReorderedProbe.Value),
            ["closed_order_by_between_keys"] = q =>
                q.OrderBy(x => x.Number).OrderBy(x => local).ThenByDescending(x => x.Category),
            ["key_after_closed_order_by"] = q => q.OrderBy(x => ReorderedProbe.Value).ThenBy(x => x.Name)
                .OrderBy(x => x.Score),
            ["where_then_reorder"] = q => q.Where(x => x.Number > 2).OrderBy(x => x.Name).OrderBy(x => x.Category),
            ["reorder_then_take"] = q => q.OrderBy(x => x.Number).OrderByDescending(x => x.Category).Take(3),
            ["reorder_then_skip_take"] = q =>
                q.OrderBy(x => x.Name).OrderBy(x => x.Score).ThenByDescending(x => x.Category).Skip(2).Take(4),
            ["string_member_then_order_by"] = q => q.OrderBy("Name").OrderBy(x => x.Category)
        };
    }
}
