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
///     Coverage for aggregate functions (Sum/Min/Max/Average) over child collections
///     inside Where() clauses, translated to correlated scalar subqueries
/// </summary>
public class collection_aggregate_predicates: IntegrationContext
{
    private List<JsonPathOrder> _orders;

    public collection_aggregate_predicates(DefaultStoreFixture fixture): base(fixture)
    {
    }

    private async Task seedOrders()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(JsonPathOrder));

        var random = new Random(20260712);
        _orders = new List<JsonPathOrder>();

        for (var i = 0; i < 40; i++)
        {
            _orders.Add(new JsonPathOrder
            {
                Id = Guid.NewGuid(),
                Lines = Enumerable.Range(0, random.Next(1, 6)).Select(_ => new JsonPathOrderLine
                {
                    ItemName = $"item-{random.Next(0, 10)}",
                    Number = random.Next(0, 101),
                    Subs = Enumerable.Range(0, random.Next(0, 4))
                        .Select(_ => new JsonPathSubLine { Amount = random.Next(0, 101) }).ToList()
                }).ToList(),
                Quantities = Enumerable.Range(0, random.Next(0, 5)).Select(_ => random.Next(0, 101)).ToList()
            });
        }

        // empty collections — Sum() must be 0, Min/Max/Average unmatched
        _orders.Add(new JsonPathOrder
        {
            Id = Guid.NewGuid(), Lines = new List<JsonPathOrderLine>(), Quantities = new List<int>()
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
    public async Task sum_with_selector()
    {
        await seedOrders();

        var sql = theSession.Query<JsonPathOrder>()
            .Where(x => x.Lines.Sum(l => l.Number) > 150)
            .ToCommand().CommandText;
        sql.ShouldContain("COALESCE(SUM(");

        await assertMatchesInMemory(
            x => x.Lines.Sum(l => l.Number) > 150,
            x => x.Lines.Sum(l => l.Number) > 150);
    }

    [Fact]
    public async Task sum_over_empty_collection_is_zero()
    {
        await seedOrders();

        // the empty-Lines doc must match == 0, exactly like LINQ-to-objects
        await assertMatchesInMemory(
            x => x.Lines.Sum(l => l.Number) == 0,
            x => x.Lines.Sum(l => l.Number) == 0);
    }

    [Fact]
    public async Task min_with_selector()
    {
        await seedOrders();

        // C# Min() over an empty collection throws; the SQL translation simply
        // fails the comparison, so guard the in-memory comparator the same way
        await assertMatchesInMemory(
            x => x.Lines.Min(l => l.Number) >= 20,
            x => x.Lines.Any() && x.Lines.Min(l => l.Number) >= 20);
    }

    [Fact]
    public async Task max_with_selector()
    {
        await seedOrders();

        await assertMatchesInMemory(
            x => x.Lines.Max(l => l.Number) < 80,
            x => x.Lines.Any() && x.Lines.Max(l => l.Number) < 80);
    }

    [Fact]
    public async Task average_with_selector()
    {
        await seedOrders();

        await assertMatchesInMemory(
            x => x.Lines.Average(l => l.Number) > 60,
            x => x.Lines.Any() && x.Lines.Average(l => l.Number) > 60);
    }

    [Fact]
    public async Task sum_over_scalar_value_collection()
    {
        await seedOrders();

        await assertMatchesInMemory(
            x => x.Quantities.Sum() > 120,
            x => x.Quantities.Sum() > 120);
    }

    [Fact]
    public async Task max_over_scalar_value_collection()
    {
        await seedOrders();

        await assertMatchesInMemory(
            x => x.Quantities.Max() > 90,
            x => x.Quantities.Any() && x.Quantities.Max() > 90);
    }

    [Fact]
    public async Task aggregate_selector_through_nested_member()
    {
        await seedOrders();

        // Subs.Count resolves to jsonb_array_length against the element
        await assertMatchesInMemory(
            x => x.Lines.Sum(l => l.Subs.Count) >= 6,
            x => x.Lines.Sum(l => l.Subs.Count) >= 6);
    }

    [Fact]
    public async Task combined_with_other_filters()
    {
        await seedOrders();

        await assertMatchesInMemory(
            x => x.Lines.Sum(l => l.Number) > 100 && x.Lines.Any(l => l.Number > 90),
            x => x.Lines.Sum(l => l.Number) > 100 && x.Lines.Any(l => l.Number > 90));
    }

    [Fact]
    public async Task aggregate_in_compiled_query_rebinds()
    {
        await seedOrders();

        var over100 = await theSession.QueryAsync(new OrdersWithLineSumOver(100));
        var over300 = await theSession.QueryAsync(new OrdersWithLineSumOver(300));

        var expected100 = _orders.Where(x => x.Lines.Sum(l => l.Number) > 100).Select(x => x.Id).OrderBy(x => x);
        var expected300 = _orders.Where(x => x.Lines.Sum(l => l.Number) > 300).Select(x => x.Id).OrderBy(x => x);

        over100.Select(x => x.Id).OrderBy(x => x).ShouldBe(expected100);
        over300.Select(x => x.Id).OrderBy(x => x).ShouldBe(expected300);
    }

    public class OrdersWithLineSumOver: ICompiledListQuery<JsonPathOrder>
    {
        public OrdersWithLineSumOver()
        {
        }

        public OrdersWithLineSumOver(int threshold)
        {
            Threshold = threshold;
        }

        public int Threshold { get; set; } = 50;

        public Expression<Func<IMartenQueryable<JsonPathOrder>, IEnumerable<JsonPathOrder>>> QueryIs()
        {
            return q => q.Where(x => x.Lines.Sum(l => l.Number) > Threshold);
        }
    }
}
