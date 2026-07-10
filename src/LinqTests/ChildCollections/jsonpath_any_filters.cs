using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.ChildCollections;

/// <summary>
///     Coverage for the jsonb_path_exists() and OR-of-containment strategies that
///     replaced the "explode + ctid" sub-query for collection Any(predicate) filters
///     that cannot become a single JSONB containment (@>) filter
/// </summary>
public class jsonpath_any_filters: IntegrationContext
{
    private List<JsonPathOrder> _orders;

    public jsonpath_any_filters(DefaultStoreFixture fixture): base(fixture)
    {
    }

    private async Task seedOrders()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(JsonPathOrder));

        var random = new Random(20260710);
        _orders = new List<JsonPathOrder>();

        for (var i = 0; i < 50; i++)
        {
            var lines = new List<JsonPathOrderLine>();
            for (var j = 0; j < 5; j++)
            {
                lines.Add(new JsonPathOrderLine
                {
                    ItemName = $"item-{random.Next(0, 20)}",
                    Number = random.Next(0, 101),
                    Subs = new List<JsonPathSubLine>
                    {
                        new() { Amount = random.Next(0, 101) }, new() { Amount = random.Next(0, 101) }
                    }
                });
            }

            _orders.Add(new JsonPathOrder
            {
                Id = Guid.NewGuid(),
                Lines = lines,
                Quantities = Enumerable.Range(0, 4).Select(_ => random.Next(0, 101)).ToList()
            });
        }

        // an element with a null member to pin down comparison-with-null semantics
        _orders.Add(new JsonPathOrder
        {
            Id = Guid.NewGuid(),
            Lines = new List<JsonPathOrderLine>
            {
                new() { ItemName = null, Number = 1, Subs = new List<JsonPathSubLine>() }
            },
            Quantities = new List<int>()
        });

        theSession.Store(_orders.ToArray());
        await theSession.SaveChangesAsync();
    }

    private async Task assertMatchesInMemory(
        Expression<Func<JsonPathOrder, bool>> filter,
        Func<JsonPathOrder, bool> inMemory)
    {
        var expected = _orders.Where(inMemory).Select(x => x.Id).OrderBy(x => x).ToArray();
        var actual = (await theSession.Query<JsonPathOrder>().Where(filter).ToListAsync())
            .Select(x => x.Id).OrderBy(x => x).ToArray();

        expected.Any().ShouldBeTrue(); // guard against vacuous assertions
        actual.ShouldBe(expected);
    }

    private string sqlFor(Expression<Func<JsonPathOrder, bool>> filter)
    {
        return theSession.Query<JsonPathOrder>().Where(filter).ToCommand().CommandText;
    }

    [Fact]
    public async Task inequality_predicate_uses_jsonpath_and_matches_in_memory()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.Any(l => l.Number > 90));
        sql.ShouldContain("jsonb_path_exists");
        sql.ShouldNotContain("ctid");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.Number > 90),
            x => x.Lines.Any(l => l.Number > 90));
    }

    [Fact]
    public async Task or_of_equalities_uses_or_of_containment()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.Any(l => l.ItemName == "item-3" || l.ItemName == "item-7"));
        sql.ShouldContain("@>");
        sql.ShouldContain(" or ");
        sql.ShouldNotContain("jsonb_path_exists");
        sql.ShouldNotContain("ctid");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.ItemName == "item-3" || l.ItemName == "item-7"),
            x => x.Lines.Any(l => l.ItemName == "item-3" || l.ItemName == "item-7"));
    }

    [Fact]
    public async Task or_with_anded_branch_uses_or_of_containment()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.Any(l =>
            l.ItemName == "item-3" || (l.ItemName == "item-7" && l.Number == 42)));
        sql.ShouldContain("@>");
        sql.ShouldNotContain("ctid");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.ItemName == "item-3" || (l.ItemName == "item-7" && l.Number == 42)),
            x => x.Lines.Any(l => l.ItemName == "item-3" || (l.ItemName == "item-7" && l.Number == 42)));
    }

    [Fact]
    public async Task mixed_and_predicate_uses_jsonpath_with_containment_prefilter()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.Any(l => l.ItemName == "item-3" && l.Number > 50));
        sql.ShouldContain("jsonb_path_exists");
        sql.ShouldContain("&&");

        // the equality conjunct also goes out as a redundant, GIN-eligible
        // containment pre-filter
        sql.ShouldContain("@>");
        sql.ShouldNotContain("ctid");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.ItemName == "item-3" && l.Number > 50),
            x => x.Lines.Any(l => l.ItemName == "item-3" && l.Number > 50));
    }

    [Fact]
    public async Task doubly_nested_mixed_and_still_flattens()
    {
        await seedOrders();

        // the nested filter must not get the containment pre-filter treatment,
        // or the outer Any() could no longer flatten it into a single jsonpath
        var sql = sqlFor(x => x.Lines.Any(l => l.Subs.Any(s => s.Amount > 90 && s.Amount != 95)));
        sql.ShouldContain("jsonb_path_exists");
        sql.ShouldContain("Lines[*].Subs[*]");
        sql.ShouldNotContain("ctid");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.Subs.Any(s => s.Amount > 90 && s.Amount != 95)),
            x => x.Lines.Any(l => l.Subs.Any(s => s.Amount > 90 && s.Amount != 95)));
    }

    [Fact]
    public async Task mixed_and_in_compiled_query_rebinds_both_parameters()
    {
        await seedOrders();

        var first = await theSession.QueryAsync(new OrdersWithItemOver("item-3", 50));
        var second = await theSession.QueryAsync(new OrdersWithItemOver("item-7", 80));

        var expectedFirst = _orders
            .Where(x => x.Lines.Any(l => l.ItemName == "item-3" && l.Number > 50))
            .Select(x => x.Id).OrderBy(x => x);
        var expectedSecond = _orders
            .Where(x => x.Lines.Any(l => l.ItemName == "item-7" && l.Number > 80))
            .Select(x => x.Id).OrderBy(x => x);

        first.Select(x => x.Id).OrderBy(x => x).ShouldBe(expectedFirst);
        second.Select(x => x.Id).OrderBy(x => x).ShouldBe(expectedSecond);
    }

    public class OrdersWithItemOver: ICompiledListQuery<JsonPathOrder>
    {
        public OrdersWithItemOver()
        {
        }

        public OrdersWithItemOver(string itemName, int threshold)
        {
            ItemName = itemName;
            Threshold = threshold;
        }

        public string ItemName { get; set; } = "item-1";
        public int Threshold { get; set; } = 10;

        public Expression<Func<IMartenQueryable<JsonPathOrder>, IEnumerable<JsonPathOrder>>> QueryIs()
        {
            return q => q.Where(x => x.Lines.Any(l => l.ItemName == ItemName && l.Number > Threshold));
        }
    }

    [Fact]
    public async Task not_equal_predicate_uses_jsonpath_with_csharp_null_semantics()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.Any(l => l.ItemName != "item-3"));
        sql.ShouldContain("jsonb_path_exists");

        // The document whose only line has ItemName == null must match, exactly
        // like LINQ-to-objects (null != "item-3"), which the old explode/ctid
        // strategy got wrong by SQL three-valued logic
        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.ItemName != "item-3"),
            x => x.Lines.Any(l => l.ItemName != "item-3"));
    }

    [Fact]
    public async Task negated_any_with_inequality_wraps_not()
    {
        await seedOrders();

        var sql = sqlFor(x => !x.Lines.Any(l => l.Number > 90));
        sql.ShouldContain("NOT(jsonb_path_exists");

        await assertMatchesInMemory(
            x => !x.Lines.Any(l => l.Number > 90),
            x => !x.Lines.Any(l => l.Number > 90));
    }

    [Fact]
    public async Task value_collection_inequality_is_now_supported()
    {
        await seedOrders();

        // used to throw BadLinqExpressionException("Marten does not (yet) support
        // the > operator in element member queries")
        var sql = sqlFor(x => x.Quantities.Any(q => q > 95));
        sql.ShouldContain("jsonb_path_exists");

        await assertMatchesInMemory(
            x => x.Quantities.Any(q => q > 95),
            x => x.Quantities.Any(q => q > 95));
    }

    [Fact]
    public async Task value_collection_or_of_equalities_uses_or_of_containment()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Quantities.Any(q => q == 3 || q == 5));
        sql.ShouldContain("@>");
        sql.ShouldNotContain("ctid");

        await assertMatchesInMemory(
            x => x.Quantities.Any(q => q == 3 || q == 5),
            x => x.Quantities.Any(q => q == 3 || q == 5));
    }

    [Fact]
    public async Task doubly_nested_any_flattens_into_one_jsonpath()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.Any(l => l.Subs.Any(s => s.Amount > 90)));
        sql.ShouldContain("jsonb_path_exists");
        sql.ShouldContain("Lines[*].Subs[*]");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.Subs.Any(s => s.Amount > 90)),
            x => x.Lines.Any(l => l.Subs.Any(s => s.Amount > 90)));
    }

    [Fact]
    public async Task equality_control_still_uses_containment()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.Any(l => l.ItemName == "item-3"));
        sql.ShouldContain("@>");
        sql.ShouldNotContain("jsonb_path_exists");
        sql.ShouldNotContain("ctid");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.ItemName == "item-3"),
            x => x.Lines.Any(l => l.ItemName == "item-3"));
    }

    [Fact]
    public async Task member_to_member_uses_correlated_exists()
    {
        await seedOrders();

        // member-vs-member comparisons have no jsonpath rendering (the right side is
        // a locator, not a constant) — they use the correlated EXISTS strategy
        var sql = sqlFor(x => x.Lines.Any(l => l.Number > l.Subs.Count));
        sql.ShouldContain("EXISTS (SELECT 1 FROM");
        sql.ShouldNotContain("ctid");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.Number > l.Subs.Count),
            x => x.Lines.Any(l => l.Number > l.Subs.Count));
    }

    [Fact]
    public async Task negated_member_to_member_uses_not_exists()
    {
        await seedOrders();

        // Number < Subs.Count is rare per line, so most documents genuinely have
        // no qualifying line and the negation is well populated
        var sql = sqlFor(x => !x.Lines.Any(l => l.Number < l.Subs.Count));
        sql.ShouldContain("NOT(EXISTS (SELECT 1 FROM");

        await assertMatchesInMemory(
            x => !x.Lines.Any(l => l.Number < l.Subs.Count),
            x => !x.Lines.Any(l => l.Number < l.Subs.Count));
    }

    [Fact]
    public async Task jsonpath_filter_in_compiled_query_rebinds_values()
    {
        await seedOrders();

        var over90 = await theSession.QueryAsync(new OrdersWithLineOver(90));
        var over99 = await theSession.QueryAsync(new OrdersWithLineOver(99));

        var expected90 = _orders.Where(x => x.Lines.Any(l => l.Number > 90)).Select(x => x.Id).OrderBy(x => x);
        var expected99 = _orders.Where(x => x.Lines.Any(l => l.Number > 99)).Select(x => x.Id).OrderBy(x => x);

        over90.Select(x => x.Id).OrderBy(x => x).ShouldBe(expected90);
        over99.Select(x => x.Id).OrderBy(x => x).ShouldBe(expected99);
    }

    public class OrdersWithLineOver: ICompiledListQuery<JsonPathOrder>
    {
        public OrdersWithLineOver()
        {
        }

        public OrdersWithLineOver(int threshold)
        {
            Threshold = threshold;
        }

        public int Threshold { get; set; } = 50;

        public Expression<Func<IMartenQueryable<JsonPathOrder>, IEnumerable<JsonPathOrder>>> QueryIs()
        {
            return q => q.Where(x => x.Lines.Any(l => l.Number > Threshold));
        }
    }
}

public class JsonPathOrder
{
    public Guid Id { get; set; }
    public List<JsonPathOrderLine> Lines { get; set; } = new();
    public List<int> Quantities { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

public class JsonPathOrderLine
{
    public string ItemName { get; set; }
    public int Number { get; set; }
    public DateTime? At { get; set; }
    public List<JsonPathSubLine> Subs { get; set; } = new();
}

public class JsonPathSubLine
{
    public int Amount { get; set; }
}
