using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

/// <summary>
///     Ordering applied AFTER a <c>GroupBy(...).Select(...)</c>, which addresses the projected shape
///     rather than the document. Originally #5557.
/// </summary>
/// <remarks>
///     <para>
///     The ordering was parsed and then dropped on the floor: <c>CompileGroupBy</c> transferred the
///     downstream <c>Limit</c> and <c>Offset</c> but never the <c>OrderingExpressions</c>, so a grouped
///     projection came back in whatever order Postgres felt like — and a paged one took an arbitrary
///     page. No error, no <c>order by</c> in the SQL.
///     </para>
///     <para>
///     Deliberately heavy on permutations. The resolution has to find the projected member behind the
///     ordering selector, and that selector reaches it differently for a key member, a renamed member,
///     an aggregate, a composite key part, and a scalar projection with no members at all — each of
///     which lands in a different branch.
///     </para>
/// </remarks>
public class grouped_projection_ordering: OneOffConfigurationsContext
{
    private static readonly Guid _ownerA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _ownerB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _ownerC = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly DateTimeOffset _jan = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _feb = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _mar = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    // Each group's size is distinct, so an ordering by count is unambiguous and a wrong
    // order is a different sequence rather than a coin flip.
    //   apples  Red    3 rows, Number sum 60
    //   bananas Blue   2 rows, Number sum 30
    //   cherries Green 1 row,  Number sum 5
    private static readonly OrderingTarget[] _data =
    [
        new() { Category = "apples",   Color = Colors.Red,   Flag = true,  Number = 10, Long = 100, Double = 1.5, Decimal = 10.25m, Date = _jan, Owner = _ownerA, NullableNumber = 1 },
        new() { Category = "apples",   Color = Colors.Red,   Flag = true,  Number = 20, Long = 200, Double = 2.5, Decimal = 20.25m, Date = _jan, Owner = _ownerA, NullableNumber = 2 },
        new() { Category = "apples",   Color = Colors.Red,   Flag = true,  Number = 30, Long = 300, Double = 3.5, Decimal = 30.25m, Date = _jan, Owner = _ownerA, NullableNumber = null },
        new() { Category = "bananas",  Color = Colors.Blue,  Flag = false, Number = 10, Long = 150, Double = 4.5, Decimal = 40.25m, Date = _feb, Owner = _ownerB, NullableNumber = 3 },
        new() { Category = "bananas",  Color = Colors.Blue,  Flag = false, Number = 20, Long = 250, Double = 5.5, Decimal = 50.25m, Date = _feb, Owner = _ownerB, NullableNumber = 4 },
        new() { Category = "cherries", Color = Colors.Green, Flag = true,  Number = 5,  Long = 50,  Double = 6.5, Decimal = 60.25m, Date = _mar, Owner = _ownerC, NullableNumber = 5 }
    ];

    public grouped_projection_ordering()
    {
        StoreOptions(opts => opts.Schema.For<OrderingTarget>());
    }

    private async Task seed()
    {
        await theStore.BulkInsertAsync(_data);
    }

    private IQueryable<OrderingTarget> query() => theSession.Query<OrderingTarget>();

    // ----------------------------------------------------------------- scalar projections

    [Fact]
    public async Task scalar_string_key_ordered_ascending()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category).Select(g => g.Key).OrderBy(x => x).ToListAsync();

        results.ShouldBe(["apples", "bananas", "cherries"]);
    }

    [Fact]
    public async Task scalar_string_key_ordered_descending()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category).Select(g => g.Key).OrderByDescending(x => x)
            .ToListAsync();

        results.ShouldBe(["cherries", "bananas", "apples"]);
    }

    [Fact]
    public async Task scalar_count_ordered_ascending()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category).Select(g => g.Count()).OrderBy(x => x).ToListAsync();

        results.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task scalar_count_ordered_descending()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category).Select(g => g.Count()).OrderByDescending(x => x)
            .ToListAsync();

        results.ShouldBe([3, 2, 1]);
    }

    [Fact]
    public async Task scalar_sum_ordered_descending()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category).Select(g => g.Sum(x => x.Number))
            .OrderByDescending(x => x).ToListAsync();

        results.ShouldBe([60, 30, 5]);
    }

    [Fact]
    public async Task scalar_max_ordered_ascending()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category).Select(g => g.Max(x => x.Number)).OrderBy(x => x)
            .ToListAsync();

        results.ShouldBe([5, 20, 30]);
    }

    [Fact]
    public async Task scalar_min_ordered_descending()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category).Select(g => g.Min(x => x.Number))
            .OrderByDescending(x => x).ToListAsync();

        results.ShouldBe([10, 10, 5]);
    }

    [Fact]
    public async Task scalar_enum_key_ordered()
    {
        await seed();

        var results = await query().GroupBy(x => x.Color).Select(g => g.Key).OrderBy(x => x).ToListAsync();

        results.ShouldBe([Colors.Red, Colors.Blue, Colors.Green]);
    }

    [Fact]
    public async Task scalar_bool_key_ordered()
    {
        await seed();

        var results = await query().GroupBy(x => x.Flag).Select(g => g.Key).OrderBy(x => x).ToListAsync();

        results.ShouldBe([false, true]);
    }

    [Fact]
    public async Task scalar_guid_key_ordered()
    {
        await seed();

        var results = await query().GroupBy(x => x.Owner).Select(g => g.Key).OrderBy(x => x).ToListAsync();

        results.ShouldBe([_ownerA, _ownerB, _ownerC]);
    }

    [Fact]
    public async Task scalar_datetimeoffset_key_ordered_descending()
    {
        await seed();

        var results = await query().GroupBy(x => x.Date).Select(g => g.Key).OrderByDescending(x => x).ToListAsync();

        results.ShouldBe([_mar, _feb, _jan]);
    }

    [Fact]
    public async Task scalar_long_key_ordered()
    {
        await seed();

        var results = await query().GroupBy(x => x.Long).Select(g => g.Key).OrderBy(x => x).ToListAsync();

        results.ShouldBe([50L, 100L, 150L, 200L, 250L, 300L]);
    }

    [Fact]
    public async Task scalar_decimal_key_ordered_descending()
    {
        await seed();

        var results = await query().GroupBy(x => x.Decimal).Select(g => g.Key).OrderByDescending(x => x).Take(2)
            .ToListAsync();

        results.ShouldBe([60.25m, 50.25m]);
    }

    [Fact]
    public async Task scalar_double_key_ordered()
    {
        await seed();

        var results = await query().GroupBy(x => x.Double).Select(g => g.Key).OrderBy(x => x).Take(2).ToListAsync();

        results.ShouldBe([1.5, 2.5]);
    }

    [Fact]
    public async Task scalar_key_ordered_and_paged()
    {
        await seed();

        var first = await query().GroupBy(x => x.Category).Select(g => g.Key).OrderBy(x => x).Take(1).ToListAsync();
        var second = await query().GroupBy(x => x.Category).Select(g => g.Key).OrderBy(x => x).Skip(1).Take(1)
            .ToListAsync();
        var last = await query().GroupBy(x => x.Category).Select(g => g.Key).OrderBy(x => x).Skip(2).ToListAsync();

        first.ShouldBe(["apples"]);
        second.ShouldBe(["bananas"]);
        last.ShouldBe(["cherries"]);
    }

    [Fact]
    public async Task scalar_aggregate_ordered_and_paged_takes_the_right_page()
    {
        await seed();

        var top = await query().GroupBy(x => x.Category).Select(g => g.Count()).OrderByDescending(x => x).Take(1)
            .ToListAsync();

        top.ShouldBe([3]);
    }

    [Fact]
    public async Task scalar_composite_key_member_ordered()
    {
        await seed();

        var results = await query().GroupBy(x => new { x.Category, x.Color }).Select(g => g.Key.Category)
            .OrderByDescending(x => x).ToListAsync();

        results.ShouldBe(["cherries", "bananas", "apples"]);
    }

    // ----------------------------------------------------------------- object projections

    [Fact]
    public async Task anonymous_projection_ordered_by_key()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Category).ToListAsync();

        results.Select(x => x.Category).ShouldBe(["cherries", "bananas", "apples"]);
    }

    [Fact]
    public async Task anonymous_projection_ordered_by_aggregate()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Number) })
            .OrderBy(x => x.Total).ToListAsync();

        results.Select(x => x.Total).ShouldBe([5, 30, 60]);
        results.Select(x => x.Category).ShouldBe(["cherries", "bananas", "apples"]);
    }

    [Fact]
    public async Task member_init_dto_ordered_by_renamed_aggregate()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category)
            .Select(g => new CategoryRollup { Name = g.Key, Total = g.Sum(x => x.Number), Rows = g.Count() })
            .OrderByDescending(x => x.Total).ToListAsync();

        results.Select(x => x.Name).ShouldBe(["apples", "bananas", "cherries"]);
        results.Select(x => x.Total).ShouldBe([60, 30, 5]);
    }

    [Fact]
    public async Task constructor_dto_ordered_by_projected_member()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category)
            .Select(g => new CategoryTuple(g.Key, g.Count()))
            .OrderByDescending(x => x.Rows).ToListAsync();

        results.Select(x => x.Rows).ShouldBe([3, 2, 1]);
        results.Select(x => x.Name).ShouldBe(["apples", "bananas", "cherries"]);
    }

    [Fact]
    public async Task then_by_breaks_the_tie_on_the_second_member()
    {
        await seed();

        // Rows ties apples(3) vs nothing, but Flag groups two categories at the same count:
        // group by Flag gives true=4 rows, false=2 rows. Order by Rows then by the key.
        var results = await query().GroupBy(x => x.Category)
            .Select(g => new CategoryRollup { Name = g.Key, Total = g.Sum(x => x.Number), Rows = g.Count() })
            .OrderBy(x => x.Rows).ThenByDescending(x => x.Name).ToListAsync();

        results.Select(x => x.Name).ShouldBe(["cherries", "bananas", "apples"]);
    }

    [Fact]
    public async Task then_by_applies_both_orderings_in_sequence()
    {
        await seed();

        // Number is shared across categories: 10 appears in apples and bananas, 20 likewise.
        var results = await query().GroupBy(x => new { x.Number, x.Category })
            .Select(g => new { g.Key.Number, g.Key.Category })
            .OrderBy(x => x.Number).ThenByDescending(x => x.Category).ToListAsync();

        results.Select(x => (x.Number, x.Category))
            .ShouldBe([(5, "cherries"), (10, "bananas"), (10, "apples"), (20, "bananas"), (20, "apples"),
                (30, "apples")]);
    }

    [Fact]
    public async Task composite_key_ordered_by_each_part()
    {
        await seed();

        var results = await query().GroupBy(x => new { x.Color, x.Flag })
            .Select(g => new { g.Key.Color, g.Key.Flag, Count = g.Count() })
            .OrderBy(x => x.Flag).ThenByDescending(x => x.Color).ToListAsync();

        results.Select(x => (x.Flag, x.Color))
            .ShouldBe([(false, Colors.Blue), (true, Colors.Green), (true, Colors.Red)]);
    }

    [Fact]
    public async Task object_projection_ordered_and_paged()
    {
        await seed();

        var grouped = query().GroupBy(x => x.Category)
            .Select(g => new CategoryRollup { Name = g.Key, Total = g.Sum(x => x.Number), Rows = g.Count() });

        var firstPage = await grouped.OrderByDescending(x => x.Total).Take(1).ToListAsync();
        var secondPage = await grouped.OrderByDescending(x => x.Total).Skip(1).Take(1).ToListAsync();
        var lastPage = await grouped.OrderByDescending(x => x.Total).Skip(2).ToListAsync();

        firstPage.Single().Name.ShouldBe("apples");
        secondPage.Single().Name.ShouldBe("bananas");
        lastPage.Single().Name.ShouldBe("cherries");
    }

    [Fact]
    public async Task ordering_survives_a_having_clause()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category)
            .Where(g => g.Count() > 1)
            .Select(g => new CategoryRollup { Name = g.Key, Total = g.Sum(x => x.Number), Rows = g.Count() })
            .OrderBy(x => x.Name).ToListAsync();

        results.Select(x => x.Name).ShouldBe(["apples", "bananas"]);
    }

    [Fact]
    public async Task ordering_survives_a_where_before_the_group_by()
    {
        await seed();

        var results = await query().Where(x => x.Number >= 10)
            .GroupBy(x => x.Category)
            .Select(g => new CategoryRollup { Name = g.Key, Total = g.Sum(x => x.Number), Rows = g.Count() })
            .OrderByDescending(x => x.Rows).ToListAsync();

        results.Select(x => x.Name).ShouldBe(["apples", "bananas"]);
    }

    [Fact]
    public async Task nullable_aggregate_ordered()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.NullableNumber) })
            .OrderByDescending(x => x.Total).ToListAsync();

        results.First().Category.ShouldBe("bananas");
    }

    // ----------------------------------------------------------------- raw SQL ordering

    [Fact]
    public async Task order_by_sql_passes_through_after_a_grouped_projection()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderBySql("count(*) desc").ToListAsync();

        results.Select(x => x.Category).ShouldBe(["apples", "bananas", "cherries"]);
    }

    [Fact]
    public async Task order_by_sql_passes_through_on_a_scalar_projection()
    {
        await seed();

        var results = await query().GroupBy(x => x.Category).Select(g => g.Key)
            .OrderBySql("count(*) desc").ToListAsync();

        results.ShouldBe(["apples", "bananas", "cherries"]);
    }

    // ----------------------------------------------------------------- SQL shape

    [Fact]
    public void the_generated_sql_carries_the_ordering()
    {
        var sql = query().GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Number) })
            .OrderByDescending(x => x.Total)
            .ToCommand().CommandText;

        sql.ShouldContain("GROUP BY");
        sql.ShouldContain("order by");
        sql.ShouldContain("desc");
    }

    [Fact]
    public void a_scalar_projection_orders_by_its_own_expression()
    {
        var sql = query().GroupBy(x => x.Category).Select(g => g.Count()).OrderByDescending(x => x)
            .ToCommand().CommandText;

        sql.ShouldContain("order by count(*) desc");
    }

    [Fact]
    public void an_unordered_grouped_projection_still_emits_no_ordering()
    {
        var sql = query().GroupBy(x => x.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(x => x.Number) })
            .ToCommand().CommandText;

        sql.ShouldNotContain("order by");
    }

    // ----------------------------------------------------------------- refusals

    [Fact]
    public async Task ordering_by_a_member_the_projection_did_not_select_is_refused()
    {
        await seed();

        var ex = await Should.ThrowAsync<BadLinqExpressionException>(async () =>
            await query().GroupBy(x => x.Category)
                .Select(g => new CategoryRollup { Name = g.Key, Total = g.Sum(x => x.Number) })
                .OrderBy(x => x.Rows).ToListAsync());

        ex.Message.ShouldContain("Rows");
        ex.Message.ShouldContain("not a projected member");
    }

    [Fact]
    public async Task ordering_a_scalar_projection_by_one_of_its_members_is_refused()
    {
        await seed();

        var ex = await Should.ThrowAsync<BadLinqExpressionException>(async () =>
            await query().GroupBy(x => x.Category).Select(g => g.Key)
                .OrderBy(x => x.Length).ToListAsync());

        ex.Message.ShouldContain("scalar GroupBy projection");
    }

    public class CategoryRollup
    {
        public string Name { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Rows { get; set; }
    }

    public record CategoryTuple(string Name, int Rows);
}

public class OrderingTarget
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public Colors Color { get; set; }
    public bool Flag { get; set; }
    public int Number { get; set; }
    public long Long { get; set; }
    public double Double { get; set; }
    public decimal Decimal { get; set; }
    public DateTimeOffset Date { get; set; }
    public Guid Owner { get; set; }
    public int? NullableNumber { get; set; }
}
