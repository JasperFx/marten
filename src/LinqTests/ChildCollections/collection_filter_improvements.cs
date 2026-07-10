using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.ChildCollections;

/// <summary>
///     Coverage for the quick-win batch: ?| for string-collection IsOneOf and
///     ICollectionAware CollectionIsEmpty (nested IsEmpty() reduction)
/// </summary>
public class collection_filter_improvements: IntegrationContext
{
    private List<JsonPathOrder> _orders;

    public collection_filter_improvements(DefaultStoreFixture fixture): base(fixture)
    {
    }

    private async Task seedOrders()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(JsonPathOrder));

        var random = new Random(20260714);
        _orders = new List<JsonPathOrder>();

        for (var i = 0; i < 30; i++)
        {
            _orders.Add(new JsonPathOrder
            {
                Id = Guid.NewGuid(),
                Tags = Enumerable.Range(0, 3).Select(_ => $"tag-{random.Next(0, 12)}").ToList(),
                Lines = Enumerable.Range(0, 3).Select(_ => new JsonPathOrderLine
                {
                    ItemName = $"item-{random.Next(0, 10)}",
                    Number = random.Next(0, 101),
                    // roughly a third of lines get an empty Subs collection
                    Subs = random.Next(0, 3) == 0
                        ? new List<JsonPathSubLine>()
                        : new List<JsonPathSubLine> { new() { Amount = random.Next(0, 101) } }
                }).ToList()
            });
        }

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
    public async Task string_collection_is_one_of_uses_jsonb_any_key_operator()
    {
        await seedOrders();

        var sql = theSession.Query<JsonPathOrder>()
            .Where(x => x.Tags.IsOneOf(new List<string> { "tag-1", "tag-2" }))
            .ToCommand().CommandText;

        sql.ShouldContain(" ?| ");
        sql.ShouldNotContain("&&");

        await assertMatchesInMemory(
            x => x.Tags.IsOneOf(new List<string> { "tag-1", "tag-2" }),
            x => x.Tags.Any(t => t == "tag-1" || t == "tag-2"));
    }

    [Fact]
    public async Task numeric_collection_is_one_of_keeps_array_overlap()
    {
        await seedOrders();

        var sql = theSession.Query<JsonPathOrder>()
            .Where(x => x.Quantities.IsOneOf(new List<int> { 1, 2, 3 }))
            .ToCommand().CommandText;

        sql.ShouldContain("&&");
    }

    [Fact]
    public async Task nested_is_empty_reduces_to_jsonpath()
    {
        await seedOrders();

        var sql = theSession.Query<JsonPathOrder>()
            .Where(x => x.Lines.Any(l => l.Subs.IsEmpty()))
            .ToCommand().CommandText;

        sql.ShouldContain("@?");
        sql.ShouldContain("size() == 0");
        sql.ShouldNotContain("ctid");

        await assertMatchesInMemory(
            x => x.Lines.Any(l => l.Subs.IsEmpty()),
            x => x.Lines.Any(l => l.Subs == null || !l.Subs.Any()));
    }

    [Fact]
    public async Task negated_nested_is_empty()
    {
        await seedOrders();

        await assertMatchesInMemory(
            x => !x.Lines.Any(l => l.Subs.IsEmpty()),
            x => !x.Lines.Any(l => l.Subs == null || !l.Subs.Any()));
    }
}
