using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.ChildCollections;

/// <summary>
///     Coverage for the second wave of jsonpath strategies: string methods
///     (starts with / like_regex), Contains() of complex elements via containment,
///     and All(predicate) via negated jsonb_path_exists
/// </summary>
public class jsonpath_string_all_contains_filters: IntegrationContext
{
    private List<JsonPathOrder> _orders;

    public jsonpath_string_all_contains_filters(DefaultStoreFixture fixture): base(fixture)
    {
    }

    private async Task seedOrders()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(JsonPathOrder));

        var random = new Random(20260711);
        _orders = new List<JsonPathOrder>();

        for (var i = 0; i < 40; i++)
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

            // a few docs carry the sentinel line used by the complex Contains() test
            if (i % 10 == 0)
            {
                lines.Add(new JsonPathOrderLine
                {
                    ItemName = "sentinel", Number = -1, Subs = new List<JsonPathSubLine>()
                });
            }

            _orders.Add(new JsonPathOrder
            {
                Id = Guid.NewGuid(),
                Lines = lines,
                Tags = Enumerable.Range(0, 3).Select(_ => $"tag-{random.Next(0, 10)}").ToList()
            });
        }

        // a line with a null member — comparison-with-null semantics; also a null
        // entry inside a scalar string collection
        _orders.Add(new JsonPathOrder
        {
            Id = Guid.NewGuid(),
            Lines = new List<JsonPathOrderLine>
            {
                new() { ItemName = null, Number = 1, Subs = new List<JsonPathSubLine>() }
            },
            Tags = new List<string> { "tag-1", null }
        });

        // an empty collection — All() must be vacuously true
        _orders.Add(new JsonPathOrder { Id = Guid.NewGuid(), Lines = new List<JsonPathOrderLine>() });

        // regex metacharacters, quotes, and backslashes — escaping coverage
        _orders.Add(new JsonPathOrder
        {
            Id = Guid.NewGuid(),
            Lines = new List<JsonPathOrderLine>
            {
                new() { ItemName = "we!rd (item) 50%", Number = 3, Subs = new List<JsonPathSubLine>() },
                new() { ItemName = "it'em ok", Number = 4, Subs = new List<JsonPathSubLine>() },
                new() { ItemName = "he said \"hi\" \\path", Number = 5, Subs = new List<JsonPathSubLine>() }
            }
        });

        // uniform names so All(name == ...) is non-vacuous
        _orders.Add(new JsonPathOrder
        {
            Id = Guid.NewGuid(),
            Lines = Enumerable.Range(60, 4).Select(n => new JsonPathOrderLine
            {
                ItemName = "uniform", Number = n, Subs = new List<JsonPathSubLine>()
            }).ToList()
        });

        // mixed casing for the case-insensitive tests
        _orders.Add(new JsonPathOrder
        {
            Id = Guid.NewGuid(),
            Lines = new List<JsonPathOrderLine>
            {
                new() { ItemName = "ITEM-MiXeD", Number = 42, Subs = new List<JsonPathSubLine>() }
            }
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
    public async Task starts_with_uses_parameterized_jsonpath()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.Any(l => l.ItemName.StartsWith("item-1")));
        sql.ShouldContain("jsonb_path_exists");
        sql.ShouldContain("starts with $");
        sql.ShouldNotContain("ctid");
        sql.ShouldNotContain("like_regex");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.ItemName.StartsWith("item-1")),
            x => x.Lines.Any(l => l.ItemName != null && l.ItemName.StartsWith("item-1")));
    }

    [Fact]
    public async Task starts_with_ignore_case_uses_like_regex()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.Any(l => l.ItemName.StartsWith("item-m", StringComparison.OrdinalIgnoreCase)));
        sql.ShouldContain("like_regex");
        sql.ShouldContain("flag \"i\"");
        sql.ShouldNotContain("ctid");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.ItemName.StartsWith("item-m", StringComparison.OrdinalIgnoreCase)),
            x => x.Lines.Any(l =>
                l.ItemName != null && l.ItemName.StartsWith("item-m", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task contains_uses_like_regex_and_escapes_metacharacters()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.Any(l => l.ItemName.Contains("(item)")));
        sql.ShouldContain("like_regex");
        sql.ShouldNotContain("ctid");

        // "(item)" must match literally, not as a regex group
        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.ItemName.Contains("(item)")),
            x => x.Lines.Any(l => l.ItemName != null && l.ItemName.Contains("(item)")));
    }

    [Fact]
    public async Task string_values_with_quotes_and_backslashes_are_safe()
    {
        await seedOrders();

        // single quote inside the search value
        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.ItemName.Contains("it'em")),
            x => x.Lines.Any(l => l.ItemName != null && l.ItemName.Contains("it'em")));

        // double quote and backslash inside the search value
        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.ItemName.Contains("said \"hi\" \\path")),
            x => x.Lines.Any(l => l.ItemName != null && l.ItemName.Contains("said \"hi\" \\path")));

        // no match at all should still run cleanly (no SQL syntax explosion)
        var none = await theSession.Query<JsonPathOrder>()
            .Where(x => x.Lines.Any(l => l.ItemName.Contains("nope'; drop table students; --")))
            .ToListAsync();
        none.ShouldBeEmpty();
    }

    [Fact]
    public async Task ends_with_uses_anchored_like_regex()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.Any(l => l.ItemName.EndsWith("-19")));
        sql.ShouldContain("like_regex");
        sql.ShouldContain("$\"");
        sql.ShouldNotContain("ctid");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.ItemName.EndsWith("-19")),
            x => x.Lines.Any(l => l.ItemName != null && l.ItemName.EndsWith("-19")));
    }

    [Fact]
    public async Task mixed_string_and_numeric_predicate_is_one_jsonpath()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.Any(l => l.ItemName.StartsWith("item-1") && l.Number > 50));
        sql.ShouldContain("jsonb_path_exists");
        sql.ShouldContain("&&");
        sql.ShouldNotContain("ctid");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.ItemName.StartsWith("item-1") && l.Number > 50),
            x => x.Lines.Any(l => l.ItemName != null && l.ItemName.StartsWith("item-1") && l.Number > 50));
    }

    [Fact]
    public async Task compiled_query_with_starts_with_rebinds()
    {
        await seedOrders();

        var one = await theSession.QueryAsync(new OrdersWithLineStartingWith("item-1"));
        var two = await theSession.QueryAsync(new OrdersWithLineStartingWith("uni"));

        var expectedOne = _orders
            .Where(x => x.Lines.Any(l => l.ItemName != null && l.ItemName.StartsWith("item-1")))
            .Select(x => x.Id).OrderBy(x => x);
        var expectedTwo = _orders
            .Where(x => x.Lines.Any(l => l.ItemName != null && l.ItemName.StartsWith("uni")))
            .Select(x => x.Id).OrderBy(x => x);

        one.Select(x => x.Id).OrderBy(x => x).ShouldBe(expectedOne);
        two.Select(x => x.Id).OrderBy(x => x).ShouldBe(expectedTwo);
    }

    [Fact]
    public async Task compiled_query_with_contains_fails_loudly()
    {
        await seedOrders();

        // the search text is embedded in the SQL literal, so a compiled query could
        // never re-bind it — this must be a descriptive error, not silently stale SQL
        var ex = await Should.ThrowAsync<BadLinqExpressionException>(async () =>
        {
            await theSession.QueryAsync(new OrdersWithLineContaining("item"));
        });

        ex.Message.ShouldContain("compiled query");
    }

    [Fact]
    public async Task complex_element_contains_uses_containment()
    {
        await seedOrders();

        var sentinel = new JsonPathOrderLine { ItemName = "sentinel", Number = -1, Subs = new List<JsonPathSubLine>() };
        var sql = sqlFor(x => x.Lines.Contains(sentinel));
        sql.ShouldContain("@>");
        sql.ShouldNotContain("ctid");

        // structural comparison in memory — Contains() by reference would never match
        await assertMatchesInMemory(
            x => x.Lines.Contains(sentinel),
            x => x.Lines.Any(l => l.ItemName == "sentinel" && l.Number == -1));
    }

    [Fact]
    public async Task all_with_inequality_uses_negated_jsonpath()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.All(l => l.Number > 10));
        sql.ShouldContain("NOT(jsonb_path_exists");
        sql.ShouldContain("!(");
        sql.ShouldNotContain("ctid");

        // the empty-Lines doc must match: All() is vacuously true
        await assertMatchesInMemory(
            x => x.Lines.All(l => l.Number > 10),
            x => x.Lines.All(l => l.Number > 10));
    }

    [Fact]
    public async Task negated_all_reverses_cleanly()
    {
        await seedOrders();

        await assertMatchesInMemory(
            x => !x.Lines.All(l => l.Number > 10),
            x => !x.Lines.All(l => l.Number > 10));
    }

    [Fact]
    public async Task all_with_equality_now_uses_jsonpath()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.All(l => l.ItemName == "uniform"));
        sql.ShouldContain("NOT(jsonb_path_exists");

        await assertMatchesInMemory(
            x => x.Lines.All(l => l.ItemName == "uniform"),
            x => x.Lines.All(l => l.ItemName == "uniform"));
    }

    [Fact]
    public async Task all_with_string_method()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.All(l => l.ItemName.StartsWith("item")));
        sql.ShouldContain("NOT(jsonb_path_exists");
        sql.ShouldContain("starts with $");

        // a null ItemName element fails the predicate on both sides
        await assertMatchesInMemory(
            x => x.Lines.All(l => l.ItemName.StartsWith("item")),
            x => x.Lines.All(l => l.ItemName != null && l.ItemName.StartsWith("item")));
    }

    [Fact]
    public async Task all_with_null_check_still_uses_legacy_path()
    {
        await seedOrders();

        // IsNullFilter is not jsonpath-capable — must keep working via the legacy shapes
        var docs = await theSession.Query<JsonPathOrder>()
            .Where(x => x.Lines.All(l => l.ItemName == null))
            .ToListAsync();

        var expected = _orders
            .Where(x => x.Lines.Any() && x.Lines.All(l => l.ItemName == null))
            .Select(x => x.Id).OrderBy(x => x).ToArray();

        expected.Any().ShouldBeTrue();
        // legacy AllMembersAreNullFilter is not vacuously true on empty collections,
        // so compare only against docs that have lines
        docs.Select(x => x.Id).OrderBy(x => x).ToArray()
            .Except(expected).Count().ShouldBeLessThanOrEqualTo(1);
        expected.Except(docs.Select(x => x.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task scalar_string_collection_starts_with()
    {
        await seedOrders();

        // Bug_834 regression shape: the element IS the string, so the jsonpath
        // reference must be the bare @, not "@."
        var sql = sqlFor(x => x.Tags.Any(t => t.StartsWith("tag-1")));
        sql.ShouldContain("jsonb_path_exists");
        sql.ShouldContain("(@ starts with $");

        await assertMatchesInMemory(
            x => x.Tags.Any(t => t.StartsWith("tag-1")),
            x => x.Tags.Any(t => t != null && t.StartsWith("tag-1")));
    }

    [Fact]
    public async Task scalar_string_collection_all_with_null_entry()
    {
        await seedOrders();

        // the doc with a null tag entry must NOT satisfy All() — the type() guard
        // makes the null element fail the predicate like it does in memory
        await assertMatchesInMemory(
            x => x.Tags.All(t => t.StartsWith("tag")),
            x => x.Tags.All(t => t != null && t.StartsWith("tag")));
    }

    [Fact]
    public async Task nested_all_inside_any_does_not_flatten()
    {
        await seedOrders();

        var sql = sqlFor(x => x.Lines.Any(l => l.Subs.All(s => s.Amount > 5)));

        // flattening NOT(exists($.Lines[*].Subs[*] ...)) would assert over every
        // line's subs — this nests through the correlated EXISTS strategy instead
        sql.ShouldContain("EXISTS (SELECT 1 FROM");
        sql.ShouldNotContain("ctid");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.Subs.All(s => s.Amount > 5)),
            x => x.Lines.Any(l => l.Subs.All(s => s.Amount > 5)));
    }

    [Fact]
    public async Task collection_filter_after_select_many_now_works()
    {
        await seedOrders();

        // a collection predicate under an already-exploded statement used to throw
        // "Sub Query filters are not supported for this operation" — the correlated
        // EXISTS strategy composes there naturally
        var fromDb = await theSession.Query<JsonPathOrder>()
            .SelectMany(x => x.Lines)
            .Where(l => l.Subs.Any(s => s.Amount > 90))
            .ToListAsync();

        var expected = _orders.SelectMany(x => x.Lines)
            .Count(l => l.Subs.Any(s => s.Amount > 90));

        expected.ShouldBeGreaterThan(0);
        fromDb.Count.ShouldBe(expected);
    }

    public class OrdersWithLineStartingWith: ICompiledListQuery<JsonPathOrder>
    {
        public OrdersWithLineStartingWith()
        {
        }

        public OrdersWithLineStartingWith(string prefix)
        {
            Prefix = prefix;
        }

        public string Prefix { get; set; } = "a";

        public Expression<Func<IMartenQueryable<JsonPathOrder>, IEnumerable<JsonPathOrder>>> QueryIs()
        {
            return q => q.Where(x => x.Lines.Any(l => l.ItemName.StartsWith(Prefix)));
        }
    }

    public class OrdersWithLineContaining: ICompiledListQuery<JsonPathOrder>
    {
        public OrdersWithLineContaining()
        {
        }

        public OrdersWithLineContaining(string fragment)
        {
            Fragment = fragment;
        }

        public string Fragment { get; set; } = "a";

        public Expression<Func<IMartenQueryable<JsonPathOrder>, IEnumerable<JsonPathOrder>>> QueryIs()
        {
            return q => q.Where(x => x.Lines.Any(l => l.ItemName.Contains(Fragment)));
        }
    }
}
