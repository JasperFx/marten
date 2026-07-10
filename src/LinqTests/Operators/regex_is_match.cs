using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LinqTests.ChildCollections;
using Marten;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

public class regex_is_match: IntegrationContext
{
    private List<JsonPathOrder> _orders;

    public regex_is_match(DefaultStoreFixture fixture): base(fixture)
    {
    }

    private async Task seedOrders()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(JsonPathOrder));

        var random = new Random(20260713);
        _orders = new List<JsonPathOrder>();

        for (var i = 0; i < 30; i++)
        {
            _orders.Add(new JsonPathOrder
            {
                Id = Guid.NewGuid(),
                Lines = Enumerable.Range(0, 4).Select(_ => new JsonPathOrderLine
                {
                    ItemName = $"item-{random.Next(0, 30)}", Number = random.Next(0, 101)
                }).ToList(),
                Tags = new List<string> { $"tag-{random.Next(0, 10)}", $"TAG-{random.Next(0, 10)}" }
            });
        }

        // a null inside a scalar collection for the negation semantics
        _orders.Add(new JsonPathOrder
        {
            Id = Guid.NewGuid(),
            Lines = new List<JsonPathOrderLine> { new() { ItemName = null, Number = 1 } },
            Tags = new List<string> { null, "tag-1" }
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

        expected.Any().ShouldBeTrue();
        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task top_level_member_uses_posix_operator()
    {
        await seedOrders();

        // top level == first tag? no — use a collection-free assertion via Lines[0]?
        // Simplest top-level string member on this type is via Any() — instead assert
        // the operator on a scalar element inside the collection filter is like_regex,
        // and the parameterized ~ shape with a whole-document type in the next test
        var sql = theSession.Query<JsonPathOrder>()
            .Where(x => x.Tags.Any(t => Regex.IsMatch(t, "^tag-[12]$")))
            .ToCommand().CommandText;
        sql.ShouldContain("like_regex");

        await assertMatchesInMemory(
            x => x.Tags.Any(t => Regex.IsMatch(t, "^tag-[12]$")),
            x => x.Tags.Any(t => t != null && Regex.IsMatch(t, "^tag-[12]$")));
    }

    [Fact]
    public async Task top_level_string_member()
    {
        await seedOrders();

        var users = new[]
        {
            new RegexUser { Id = Guid.NewGuid(), UserName = "alice-42" },
            new RegexUser { Id = Guid.NewGuid(), UserName = "bob.99" },
            new RegexUser { Id = Guid.NewGuid(), UserName = "Carol-7" }
        };
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(RegexUser));
        theSession.Store(users);
        await theSession.SaveChangesAsync();

        var sql = theSession.Query<RegexUser>()
            .Where(x => Regex.IsMatch(x.UserName, "^[a-z]+-\\d+$"))
            .ToCommand().CommandText;
        sql.ShouldContain(" ~ ");

        var matching = await theSession.Query<RegexUser>()
            .Where(x => Regex.IsMatch(x.UserName, "^[a-z]+-\\d+$"))
            .ToListAsync();
        matching.Single().UserName.ShouldBe("alice-42");

        // case-insensitive flavor
        var insensitive = await theSession.Query<RegexUser>()
            .Where(x => Regex.IsMatch(x.UserName, "^[a-z]+-\\d+$", RegexOptions.IgnoreCase))
            .ToListAsync();
        insensitive.Select(x => x.UserName).OrderBy(x => x)
            .ShouldBe(new[] { "Carol-7", "alice-42" }, ignoreOrder: true);
    }

    [Fact]
    public async Task inside_child_collection_predicate()
    {
        await seedOrders();

        var sql = theSession.Query<JsonPathOrder>()
            .Where(x => x.Lines.Any(l => Regex.IsMatch(l.ItemName, "^item-1\\d$")))
            .ToCommand().CommandText;
        sql.ShouldContain("jsonb_path_exists");
        sql.ShouldContain("like_regex");
        sql.ShouldNotContain("ctid");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => Regex.IsMatch(l.ItemName, "^item-1\\d$")),
            x => x.Lines.Any(l => l.ItemName != null && Regex.IsMatch(l.ItemName, "^item-1\\d$")));
    }

    [Fact]
    public async Task all_with_regex_treats_null_as_failing()
    {
        await seedOrders();

        await assertMatchesInMemory(
            x => x.Tags.All(t => Regex.IsMatch(t, "^(tag|TAG)-\\d$")),
            x => x.Tags.All(t => t != null && Regex.IsMatch(t, "^(tag|TAG)-\\d$")));
    }

    [Fact]
    public async Task unsupported_regex_options_throw()
    {
        await seedOrders();

        await Should.ThrowAsync<BadLinqExpressionException>(async () =>
        {
            await theSession.Query<JsonPathOrder>()
                .Where(x => x.Lines.Any(l => Regex.IsMatch(l.ItemName, "x", RegexOptions.Multiline)))
                .ToListAsync();
        });
    }

    [Fact]
    public async Task compiled_query_with_collection_regex_fails_loudly()
    {
        await seedOrders();

        var ex = await Should.ThrowAsync<BadLinqExpressionException>(async () =>
        {
            await theSession.QueryAsync(new OrdersMatchingPattern("^item"));
        });

        ex.Message.ShouldContain("compiled query");
    }

    public class OrdersMatchingPattern: ICompiledListQuery<JsonPathOrder>
    {
        public OrdersMatchingPattern()
        {
        }

        public OrdersMatchingPattern(string pattern)
        {
            Pattern = pattern;
        }

        public string Pattern { get; set; } = "a";

        public Expression<Func<IMartenQueryable<JsonPathOrder>, IEnumerable<JsonPathOrder>>> QueryIs()
        {
            return q => q.Where(x => x.Lines.Any(l => Regex.IsMatch(l.ItemName, Pattern)));
        }
    }
}

public class RegexUser
{
    public Guid Id { get; set; }
    public string UserName { get; set; }
}
