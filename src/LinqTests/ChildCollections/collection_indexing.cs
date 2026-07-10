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
///     Indexing into complex child collections inside Where() clauses:
///     x.Lines[0].Name, x.Lines.ElementAt(n).Number, and List indexers on
///     scalar collections
/// </summary>
public class collection_indexing: IntegrationContext
{
    private List<JsonPathOrder> _orders;

    public collection_indexing(DefaultStoreFixture fixture): base(fixture)
    {
    }

    private async Task seedOrders()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(JsonPathOrder));

        var random = new Random(20260716);
        _orders = new List<JsonPathOrder>();

        for (var i = 0; i < 30; i++)
        {
            _orders.Add(new JsonPathOrder
            {
                Id = Guid.NewGuid(),
                Lines = Enumerable.Range(0, random.Next(1, 4)).Select(n => new JsonPathOrderLine
                {
                    ItemName = $"item-{random.Next(0, 10)}", Number = random.Next(0, 101)
                }).ToList(),
                Tags = Enumerable.Range(0, random.Next(1, 4)).Select(_ => $"tag-{random.Next(0, 10)}").ToList()
            });
        }

        // an empty one — out-of-range index access must simply not match
        _orders.Add(new JsonPathOrder
        {
            Id = Guid.NewGuid(), Lines = new List<JsonPathOrderLine>(), Tags = new List<string>()
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
    public async Task list_indexer_into_complex_element_member()
    {
        await seedOrders();

        var sql = theSession.Query<JsonPathOrder>()
            .Where(x => x.Lines[0].ItemName == "item-3")
            .ToCommand().CommandText;
        sql.ShouldContain("-> 'Lines' -> 0 ->> 'ItemName'");

        await assertMatchesInMemory(
            x => x.Lines[0].ItemName == "item-3",
            x => x.Lines.Count > 0 && x.Lines[0].ItemName == "item-3");
    }

    [Fact]
    public async Task list_indexer_with_numeric_comparison()
    {
        await seedOrders();

        await assertMatchesInMemory(
            x => x.Lines[1].Number > 50,
            x => x.Lines.Count > 1 && x.Lines[1].Number > 50);
    }

    [Fact]
    public async Task element_at_into_complex_element()
    {
        await seedOrders();

        await assertMatchesInMemory(
            x => x.Lines.ElementAt(1).Number > 50,
            x => x.Lines.Count > 1 && x.Lines[1].Number > 50);
    }

    [Fact]
    public async Task list_indexer_on_scalar_collection()
    {
        await seedOrders();

        await assertMatchesInMemory(
            x => x.Tags[0] == "tag-4",
            x => x.Tags.Count > 0 && x.Tags[0] == "tag-4");
    }

    [Fact]
    public async Task variable_index_reduces_to_constant()
    {
        await seedOrders();

        var index = 0;
        await assertMatchesInMemory(
            x => x.Lines[index].ItemName == "item-3",
            x => x.Lines.Count > 0 && x.Lines[0].ItemName == "item-3");
    }

    [Fact]
    public async Task out_of_range_index_simply_does_not_match()
    {
        await seedOrders();

        // no document has ten lines — json -> 9 yields SQL NULL, never a match or error
        var docs = await theSession.Query<JsonPathOrder>()
            .Where(x => x.Lines[9].ItemName == "item-3")
            .ToListAsync();
        docs.ShouldBeEmpty();
    }

    [Fact]
    public async Task indexed_element_null_check()
    {
        await seedOrders();

        // "second line is absent" — matches docs with fewer than two lines
        await assertMatchesInMemory(
            x => x.Lines[1] == null,
            x => x.Lines.Count <= 1);
    }
}
