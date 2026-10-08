using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Operators;

public class group_by_predicate_aggregates: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public group_by_predicate_aggregates(ITestOutputHelper output)
    {
        _output = output;
    }

    private async Task seed(bool duplicated)
    {
        StoreOptions(options =>
        {
            var mapping = options.Schema.For<GroupByTarget>().MultiTenanted();
            if (duplicated)
            {
                mapping.Duplicate(x => x.Decimal);
            }
        });

        var targets = new[]
        {
            target(Colors.Green, "Gamma", null, 20),
            target(Colors.Purple, "Delta", 0m, 5),
            target(Colors.Blue, "Gamma", 11m, 40),
            target(Colors.Blue, "Beta", 7m, 30),
            target(Colors.Blue, "Beta", null, 30),
            target(Colors.Blue, "Beta", null, 30),
            target(Colors.Red, "Alpha", 2m, 20),
            target(Colors.Red, "Alpha", 3m, 20),
            target(Colors.Green, "Gamma", null, 20),
            target(Colors.Blue, "Beta", null, 0)
        };

        using var session = theStore.LightweightSession("tenant1");
        session.Store(targets);
        await session.SaveChangesAsync();

        using var other = theStore.LightweightSession("tenant2");
        other.Store(targets.Select(x => new GroupByTarget
        {
            Id = x.Id, Color = x.Color, String = x.String, Decimal = 100m,
            Number = x.Number, Long = x.Long, Flag = true
        }).ToArray());
        await other.SaveChangesAsync();
    }

    private static GroupByTarget target(Colors color, string text, decimal? value, int number)
    {
        return new GroupByTarget
        {
            Color = color, String = text, Decimal = value,
            Number = number, Long = number, Flag = value != null
        };
    }

    private void sql<T>(IQueryable<T> query)
    {
        using var command = query.ToCommand();
        _output.WriteLine(command.CommandText);
        foreach (Npgsql.NpgsqlParameter parameter in command.Parameters)
        {
            _output.WriteLine($"{parameter.ParameterName}: {parameter.Value}");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task scalar_key_count_null_predicate(bool duplicated)
    {
        await seed(duplicated);
        using var session = theStore.QuerySession("tenant1");
        var query = session.Query<GroupByTarget>().Where(x => x.Number > 0)
            .GroupBy(x => x.Color)
            .Select(g => new CountRow { Category = g.Key, Count = g.Count(x => x.Decimal == null) });
        sql(query);
        var rows = await query.OrderBy(x => x.Category).ToListAsync();
        rows.Select(x => (x.Category, x.Count))
            .ShouldBe(new[] { (Colors.Red, 0), (Colors.Blue, 2), (Colors.Green, 2), (Colors.Purple, 0) });
        using var command = query.ToCommand();
        command.CommandText.ShouldContain("is null");
        command.CommandText.ShouldNotContain("= True");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task scalar_key_long_count_null_predicate(bool duplicated)
    {
        await seed(duplicated);
        using var session = theStore.QuerySession("tenant1");
        var query = session.Query<GroupByTarget>().Where(x => x.Number > 0)
            .GroupBy(x => x.Color)
            .Select(g => new LongCountRow { Category = g.Key, Count = g.LongCount(x => x.Decimal == null) });
        sql(query);
        var rows = await query.OrderBy(x => x.Category).ToListAsync();
        rows.Select(x => (x.Category, x.Count))
            .ShouldBe(new[] { (Colors.Red, 0L), (Colors.Blue, 2L), (Colors.Green, 2L), (Colors.Purple, 0L) });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task composite_projection_sums_counts_tenants_and_grouped_paging(bool duplicated)
    {
        await seed(duplicated);
        using var session = theStore.QuerySession("tenant1");
        var grouped = session.Query<GroupByTarget>().Where(x => x.Number > 0)
            .GroupBy(x => new { x.Color, x.String })
            .Select(g => new GroupedTarget
            {
                Category = g.Key.Color,
                Label = g.Key.String,
                DecimalTotal = g.Sum(x => x.Decimal),
                NullCount = g.Count(x => x.Decimal == null),
                LongNullCount = g.LongCount(x => x.Decimal == null),
                Total = g.Sum(x => x.Long)
            });

        var ordered = grouped.OrderByDescending(x => x.Total)
            .ThenBy(x => x.Category).ThenBy(x => x.Label);
        sql(ordered);
        var rows = await ordered.ToListAsync();
        rows.Select(x => (x.Category, x.Label, x.NullCount, x.LongNullCount, x.DecimalTotal, x.Total))
            .ShouldBe(new[]
            {
                (Colors.Blue, "Beta", 2, 2L, (decimal?)7m, 90L),
                (Colors.Red, "Alpha", 0, 0L, (decimal?)5m, 40L),
                (Colors.Blue, "Gamma", 0, 0L, (decimal?)11m, 40L),
                (Colors.Green, "Gamma", 2, 2L, (decimal?)null, 40L),
                (Colors.Purple, "Delta", 0, 0L, (decimal?)0m, 5L)
            });

        var page = ordered.Skip(1).Take(2);
        sql(page);
        using (var command = page.ToCommand())
        {
            command.CommandText.ShouldContain("GROUP BY");
            command.CommandText.ShouldContain("order by sum(");
            command.CommandText.ShouldContain("OFFSET");
            command.CommandText.ShouldContain("LIMIT");
        }
        var paged = await page.ToListAsync();
        paged.Select(x => (x.Category, x.Label, x.NullCount, x.DecimalTotal, x.Total))
            .ShouldBe(new[]
            {
                (Colors.Red, "Alpha", 0, (decimal?)5m, 40L),
                (Colors.Blue, "Gamma", 0, (decimal?)11m, 40L)
            });
        (await grouped.CountAsync()).ShouldBe(5);
        (await grouped.LongCountAsync()).ShouldBe(5L);

        using var other = theStore.QuerySession("tenant2");
        var otherRows = await other.Query<GroupByTarget>().Where(x => x.Number > 0)
            .GroupBy(x => new { x.Color, x.String })
            .Select(g => new GroupedTarget
            {
                Category = g.Key.Color, Label = g.Key.String,
                DecimalTotal = g.Sum(x => x.Decimal),
                NullCount = g.Count(x => x.Decimal == null),
                LongNullCount = g.LongCount(x => x.Decimal == null),
                Total = g.Sum(x => x.Long)
            }).ToListAsync();
        otherRows.Count.ShouldBe(5);
        otherRows.ShouldAllBe(x => x.NullCount == 0 && x.LongNullCount == 0);
        otherRows.Single(x => x.Category == Colors.Blue && x.Label == "Beta").DecimalTotal.ShouldBe(300m);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task predicates_use_normal_where_translation_and_parameters(bool duplicated)
    {
        await seed(duplicated);
        using var session = theStore.QuerySession("tenant1");
        var threshold = 2m;
        var query = session.Query<GroupByTarget>().Where(x => x.Number > 0)
            .GroupBy(x => x.Color)
            .Select(g => new
            {
                Category = g.Key,
                Present = g.Count(x => x.Decimal != null),
                LongPresent = g.LongCount(x => x.Decimal != null),
                Above = g.Count(x => x.Decimal > threshold),
                LongAbove = g.LongCount(x => x.Decimal > threshold),
                Compound = g.Count(x => (x.Decimal == null || x.Decimal > threshold) && x.Number >= 20),
                LongCompound = g.LongCount(x => (x.Decimal == null || x.Decimal > threshold) && x.Number >= 20),
                Flagged = g.Count(x => x.Flag),
                LongFlagged = g.LongCount(x => x.Flag)
            });
        sql(query);
        using (var command = query.ToCommand())
        {
            command.Parameters.Cast<Npgsql.NpgsqlParameter>().ShouldContain(x => Equals(x.Value, threshold));
        }

        var rows = await query.OrderBy(x => x.Category).ToListAsync();
        rows.Select(x => (x.Category, x.Present, x.Above, x.Compound, x.Flagged))
            .ShouldBe(new[]
            {
                (Colors.Red, 2, 1, 1, 2), (Colors.Blue, 2, 2, 4, 2),
                (Colors.Green, 0, 0, 2, 0), (Colors.Purple, 1, 0, 0, 1)
            });
        rows.ShouldAllBe(x => x.LongPresent == x.Present && x.LongAbove == x.Above &&
                              x.LongCompound == x.Compound && x.LongFlagged == x.Flagged);

        var source = session.Query<GroupByTarget>().Where(x => x.Number > 0);
        (await source.CountAsync(x => x.Decimal == null)).ShouldBe(4);
        (await source.LongCountAsync(x => x.Decimal != null)).ShouldBe(5L);
        (await source.CountAsync(x => x.Decimal > threshold)).ShouldBe(3);
        (await source.LongCountAsync(x => (x.Decimal == null || x.Decimal > threshold) && x.Number >= 20)).ShouldBe(7L);
        (await source.CountAsync(x => x.Flag)).ShouldBe(5);

        var orderedCounts = await query.OrderByDescending(x => x.Above).ThenBy(x => x.Category).Skip(1).Take(2).ToListAsync();
        orderedCounts.Select(x => (x.Category, x.Above)).ShouldBe(new[] { (Colors.Red, 1), (Colors.Green, 0) });
        threshold = 8m;
        var rebound = await query.OrderBy(x => x.Category).ToListAsync();
        rebound.Select(x => x.Above).ShouldBe(new[] { 0, 1, 0, 0 });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task scalar_aggregate_projections_keep_count_and_long_count_types(bool duplicated)
    {
        await seed(duplicated);
        using var session = theStore.QuerySession("tenant1");
        var groups = session.Query<GroupByTarget>().Where(x => x.Number > 0).GroupBy(x => x.Color);
        var counts = groups.Select(g => g.Count(x => x.Decimal == null));
        sql(counts);
        (await counts.OrderBy(x => x).ToListAsync()).ShouldBe(new[] { 0, 0, 2, 2 });
        (await groups.Select(g => g.LongCount(x => x.Decimal == null)).OrderBy(x => x).ToListAsync())
            .ShouldBe(new[] { 0L, 0L, 2L, 2L });
        (await groups.Select(g => g.LongCount()).OrderBy(x => x).ToListAsync()).ShouldBe(new[] { 1L, 2L, 2L, 4L });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task compiled_group_predicates_rebind_values_and_tenants(bool duplicated)
    {
        await seed(duplicated);
        using var session = theStore.QuerySession("tenant1");
        var first = await session.QueryAsync(new AboveThreshold { Threshold = 2m });
        first.Select(x => (x.Category, x.Count))
            .ShouldBe(new[] { (Colors.Blue, 2), (Colors.Red, 1), (Colors.Green, 0), (Colors.Purple, 0) });
        var second = await session.QueryAsync(new AboveThreshold { Threshold = 8m });
        second.Select(x => (x.Category, x.Count))
            .ShouldBe(new[] { (Colors.Blue, 1), (Colors.Red, 0), (Colors.Green, 0), (Colors.Purple, 0) });
        using var other = theStore.QuerySession("tenant2");
        var otherRows = await other.QueryAsync(new AboveThreshold { Threshold = 8m });
        otherRows.Select(x => (x.Category, x.Count))
            .ShouldBe(new[] { (Colors.Blue, 4), (Colors.Red, 2), (Colors.Green, 2), (Colors.Purple, 1) });
    }

    [Fact]
    public async Task duplicated_nullable_has_value_remains_explicitly_unsupported()
    {
        await seed(true);
        using var session = theStore.QuerySession("tenant1");
        var exception = Should.Throw<BadLinqExpressionException>(() => session.Query<GroupByTarget>()
            .GroupBy(x => x.Color)
            .Select(g => new CountRow { Category = g.Key, Count = g.Count(x => x.Decimal.HasValue) })
            .ToCommand());
        _output.WriteLine(exception.Message);
    }

    [Fact]
    public async Task boolean_predicates_over_grouped_joins_keep_their_cte_aliases()
    {
        await seed(true);
        using var session = theStore.QuerySession("tenant1");
        var source = session.Query<GroupByTarget>().Where(x => x.Number > 0);
        var joined = source.GroupJoin(source, x => x.Id, x => x.Id, (left, matches) => new { left, matches })
            .SelectMany(x => x.matches, (x, right) => new
            {
                x.left.Color, right.Flag, right.Decimal
            }).GroupBy(x => x.Color);
        var query = joined.Select(g => new
        {
            Category = g.Key,
            Flagged = g.Count(x => x.Flag),
            LongFlagged = g.LongCount(x => x.Flag)
        });
        sql(query);
        var rows = await query.ToListAsync();
        rows.Count.ShouldBe(4);
        rows.Single(x => x.Category == Colors.Red).Flagged.ShouldBe(2);
        rows.Single(x => x.Category == Colors.Blue).Flagged.ShouldBe(2);
        rows.Single(x => x.Category == Colors.Green).Flagged.ShouldBe(0);
        rows.Single(x => x.Category == Colors.Purple).Flagged.ShouldBe(1);
        rows.ShouldAllBe(x => x.LongFlagged == x.Flagged);

        Should.Throw<BadLinqExpressionException>(() => joined
            .Select(g => new { Category = g.Key, NullCount = g.Count(x => x.Decimal == null) })
            .ToCommand());
    }

    // Target-like fields, with a nullable Decimal to exercise numeric null predicates.
    public class GroupByTarget
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Colors Color { get; set; }
        public string String { get; set; } = string.Empty;
        public decimal? Decimal { get; set; }
        public int Number { get; set; }
        public long Long { get; set; }
        public bool Flag { get; set; }
    }

    public class AboveThreshold: ICompiledQuery<GroupByTarget, IEnumerable<CountRow>>
    {
        public decimal Threshold { get; set; }

        public Expression<Func<IMartenQueryable<GroupByTarget>, IEnumerable<CountRow>>> QueryIs()
        {
            return query => query.Where(x => x.Number > 0).GroupBy(x => x.Color)
                .Select(g => new CountRow
                {
                    Category = g.Key,
                    Count = g.Count(x => x.Decimal > Threshold)
                }).OrderByDescending(x => x.Count).ThenBy(x => x.Category);
        }
    }

    public class CountRow
    {
        public Colors Category { get; set; }
        public int Count { get; set; }
    }

    public class LongCountRow
    {
        public Colors Category { get; set; }
        public long Count { get; set; }
    }

    public class GroupedTarget
    {
        public Colors Category { get; set; }
        public string Label { get; set; } = string.Empty;
        public decimal? DecimalTotal { get; set; }
        public int NullCount { get; set; }
        public long LongNullCount { get; set; }
        public long Total { get; set; }
    }
}
