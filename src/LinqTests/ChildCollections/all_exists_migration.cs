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
///     The All() shapes the jsonpath tier rejects — null checks and DateTime
///     comparisons — now translate to NOT EXISTS over the exploded elements, and
///     duplicated array fields participate in the EXISTS strategy
/// </summary>
public class all_exists_migration: OneOffConfigurationsContext
{
    private List<JsonPathOrder> _orders;

    private async Task seedOrders()
    {
        var random = new Random(20260715);
        _orders = new List<JsonPathOrder>();

        var baseline = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        for (var i = 0; i < 30; i++)
        {
            _orders.Add(new JsonPathOrder
            {
                Id = Guid.NewGuid(),
                Lines = Enumerable.Range(0, random.Next(1, 4)).Select(_ => new JsonPathOrderLine
                {
                    ItemName = random.Next(0, 4) == 0 ? null : $"item-{random.Next(0, 10)}",
                    Number = random.Next(0, 101),
                    At = random.Next(0, 5) == 0 ? null : baseline.AddDays(random.Next(0, 60))
                }).ToList()
            });
        }

        // empty collection — All() is vacuously true
        _orders.Add(new JsonPathOrder { Id = Guid.NewGuid(), Lines = new List<JsonPathOrderLine>() });

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
    public async Task all_with_null_check_uses_not_exists()
    {
        await seedOrders();

        var sql = theSession.Query<JsonPathOrder>()
            .Where(x => x.Lines.All(l => l.ItemName == null))
            .ToCommand().CommandText;
        sql.ShouldContain("NOT(EXISTS (SELECT 1 FROM");
        sql.ShouldContain("is not null");
        sql.ShouldNotContain("ctid");

        // vacuously true for the empty-Lines doc, like LINQ-to-objects
        await assertMatchesInMemory(
            x => x.Lines.All(l => l.ItemName == null),
            x => x.Lines.All(l => l.ItemName == null));
    }

    [Fact]
    public async Task all_with_not_null_check_uses_not_exists()
    {
        await seedOrders();

        var sql = theSession.Query<JsonPathOrder>()
            .Where(x => x.Lines.All(l => l.ItemName != null))
            .ToCommand().CommandText;
        sql.ShouldContain("NOT(EXISTS (SELECT 1 FROM");

        await assertMatchesInMemory(
            x => x.Lines.All(l => l.ItemName != null),
            x => x.Lines.All(l => l.ItemName != null));
    }

    [Fact]
    public async Task all_with_datetime_comparison_uses_not_exists()
    {
        await seedOrders();

        var pivot = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Unspecified);

        var sql = theSession.Query<JsonPathOrder>()
            .Where(x => x.Lines.All(l => l.At < pivot))
            .ToCommand().CommandText;
        sql.ShouldContain("NOT(EXISTS (SELECT 1 FROM");
        sql.ShouldNotContain("ctid");

        // a null At fails the comparison on both sides, and the null-safe failing
        // predicate (is null OR >=) keeps that semantic in SQL
        await assertMatchesInMemory(
            x => x.Lines.All(l => l.At < pivot),
            x => x.Lines.All(l => l.At.HasValue && l.At < pivot));
    }

    [Fact]
    public async Task value_collection_datetime_inequality_now_works()
    {
        // DateTime element predicates are rejected by the jsonpath tier (serialized
        // date strings do not compare reliably) and previously had NO working
        // strategy — ElementComparisonFilter could not render itself as plain SQL
        var baseline = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var docs = Enumerable.Range(0, 15).Select(i => new DateListDoc
        {
            Id = Guid.NewGuid(),
            Stamps = new List<DateTime> { baseline.AddDays(i), baseline.AddDays(i + 30) }
        }).ToArray();
        await theStore.BulkInsertDocumentsAsync(docs);

        var pivot = baseline.AddDays(40);
        var expected = docs.Where(x => x.Stamps.Any(s => s > pivot)).Select(x => x.Id).OrderBy(x => x).ToArray();
        var actual = (await theSession.Query<DateListDoc>().Where(x => x.Stamps.Any(s => s > pivot)).ToListAsync())
            .Select(x => x.Id).OrderBy(x => x).ToArray();

        expected.Any().ShouldBeTrue();
        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task duplicated_array_field_inequality_uses_exists()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<DupArrayDoc>().Duplicate(x => x.Scores);
        });

        var docs = Enumerable.Range(0, 20).Select(i => new DupArrayDoc
        {
            Id = Guid.NewGuid(), Scores = new[] { i, i * 3, 100 - i }
        }).ToArray();
        await theStore.BulkInsertDocumentsAsync(docs);

        var sql = theSession.Query<DupArrayDoc>()
            .Where(x => x.Scores.Any(s => s > 90))
            .ToCommand().CommandText;
        sql.ShouldContain("EXISTS (SELECT 1 FROM");
        sql.ShouldContain("unnest(");
        sql.ShouldNotContain("ctid");

        var expected = docs.Where(x => x.Scores.Any(s => s > 90)).Select(x => x.Id).OrderBy(x => x).ToArray();
        var actual = (await theSession.Query<DupArrayDoc>().Where(x => x.Scores.Any(s => s > 90)).ToListAsync())
            .Select(x => x.Id).OrderBy(x => x).ToArray();

        expected.Any().ShouldBeTrue();
        actual.ShouldBe(expected);
    }
}

public class DateListDoc
{
    public Guid Id { get; set; }
    public List<DateTime> Stamps { get; set; } = new();
}

public class DupArrayDoc
{
    public Guid Id { get; set; }
    public int[] Scores { get; set; }
}
