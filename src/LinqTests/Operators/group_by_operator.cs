using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

public class group_by_operator: OneOffConfigurationsContext
{
    private IDocumentStore _store;
    private IDocumentSession _session;

    private async Task SetupTargetData()
    {
        _store = StoreOptions(opts =>
        {
            opts.Schema.For<Target>();
        });

        _session = _store.LightweightSession();
        _disposables.Add(_session);

        // Deterministic data for GroupBy tests
        var targets = new[]
        {
            new Target { Color = Colors.Blue, Number = 10, String = "Alpha", Double = 1.5 },
            new Target { Color = Colors.Blue, Number = 20, String = "Alpha", Double = 2.5 },
            new Target { Color = Colors.Green, Number = 30, String = "Beta", Double = 3.5 },
            new Target { Color = Colors.Green, Number = 40, String = "Beta", Double = 4.5 },
            new Target { Color = Colors.Green, Number = 50, String = "Gamma", Double = 5.5 },
            new Target { Color = Colors.Red, Number = 60, String = "Gamma", Double = 6.5 },
        };

        _session.Store(targets);
        await _session.SaveChangesAsync();
    }

    #region sample_group_by_simple_key_with_count

    [Fact]
    public async Task group_by_simple_key_with_count()
    {
        await SetupTargetData();

        var results = await _session.Query<Target>()
            .GroupBy(x => x.Color)
            .Select(g => new { Color = g.Key, Count = g.Count() })
            .ToListAsync();

        results.Count.ShouldBe(3);
        results.Single(x => x.Color == Colors.Blue).Count.ShouldBe(2);
        results.Single(x => x.Color == Colors.Green).Count.ShouldBe(3);
        results.Single(x => x.Color == Colors.Red).Count.ShouldBe(1);
    }

    #endregion

    [Fact]
    public async Task group_by_simple_key_with_sum()
    {
        await SetupTargetData();

        var results = await _session.Query<Target>()
            .GroupBy(x => x.Color)
            .Select(g => new { Color = g.Key, Total = g.Sum(x => x.Number) })
            .ToListAsync();

        results.Count.ShouldBe(3);
        results.Single(x => x.Color == Colors.Blue).Total.ShouldBe(30);
        results.Single(x => x.Color == Colors.Green).Total.ShouldBe(120);
        results.Single(x => x.Color == Colors.Red).Total.ShouldBe(60);
    }

    [Fact]
    public async Task group_by_simple_key_with_multiple_aggregates()
    {
        await SetupTargetData();

        var results = await _session.Query<Target>()
            .GroupBy(x => x.Color)
            .Select(g => new
            {
                Color = g.Key,
                Count = g.Count(),
                Total = g.Sum(x => x.Number),
                Min = g.Min(x => x.Number),
                Max = g.Max(x => x.Number)
            })
            .ToListAsync();

        results.Count.ShouldBe(3);

        var blue = results.Single(x => x.Color == Colors.Blue);
        blue.Count.ShouldBe(2);
        blue.Total.ShouldBe(30);
        blue.Min.ShouldBe(10);
        blue.Max.ShouldBe(20);

        var green = results.Single(x => x.Color == Colors.Green);
        green.Count.ShouldBe(3);
        green.Total.ShouldBe(120);
        green.Min.ShouldBe(30);
        green.Max.ShouldBe(50);
    }

    [Fact]
    public async Task group_by_string_key()
    {
        await SetupTargetData();

        var results = await _session.Query<Target>()
            .GroupBy(x => x.String)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync();

        results.Count.ShouldBe(3);
        results.Single(x => x.Key == "Alpha").Count.ShouldBe(2);
        results.Single(x => x.Key == "Beta").Count.ShouldBe(2);
        results.Single(x => x.Key == "Gamma").Count.ShouldBe(2);
    }

    [Fact]
    public async Task group_by_with_where_before_group()
    {
        await SetupTargetData();

        var results = await _session.Query<Target>()
            .Where(x => x.Number > 20)
            .GroupBy(x => x.Color)
            .Select(g => new { Color = g.Key, Count = g.Count() })
            .ToListAsync();

        // Blue has 10, 20 -> both filtered out
        // Green has 30, 40, 50 -> 3 pass
        // Red has 60 -> 1 passes
        results.Count.ShouldBe(2);
        results.Single(x => x.Color == Colors.Green).Count.ShouldBe(3);
        results.Single(x => x.Color == Colors.Red).Count.ShouldBe(1);
    }

    [Fact]
    public async Task group_by_with_having()
    {
        await SetupTargetData();

        var results = await _session.Query<Target>()
            .GroupBy(x => x.Color)
            .Where(g => g.Count() > 1)
            .Select(g => new { Color = g.Key, Count = g.Count() })
            .ToListAsync();

        // Blue=2, Green=3, Red=1 -> Red filtered by HAVING
        results.Count.ShouldBe(2);
        results.ShouldNotContain(x => x.Color == Colors.Red);
    }

    [Fact]
    public async Task group_by_composite_key()
    {
        await SetupTargetData();

        var results = await _session.Query<Target>()
            .GroupBy(x => new { x.Color, x.String })
            .Select(g => new { Color = g.Key.Color, Text = g.Key.String, Count = g.Count() })
            .ToListAsync();

        // Blue+Alpha=2, Green+Beta=2, Green+Gamma=1, Red+Gamma=1
        results.Count.ShouldBe(4);
        results.Single(x => x.Color == Colors.Blue && x.Text == "Alpha").Count.ShouldBe(2);
        results.Single(x => x.Color == Colors.Green && x.Text == "Beta").Count.ShouldBe(2);
        results.Single(x => x.Color == Colors.Green && x.Text == "Gamma").Count.ShouldBe(1);
        results.Single(x => x.Color == Colors.Red && x.Text == "Gamma").Count.ShouldBe(1);
    }

    [Fact]
    public async Task group_by_with_average()
    {
        await SetupTargetData();

        var results = await _session.Query<Target>()
            .GroupBy(x => x.Color)
            .Select(g => new { Color = g.Key, Avg = g.Average(x => x.Double) })
            .ToListAsync();

        results.Count.ShouldBe(3);
        results.Single(x => x.Color == Colors.Blue).Avg.ShouldBe(2.0, tolerance: 0.01);
        results.Single(x => x.Color == Colors.Green).Avg.ShouldBe(4.5, tolerance: 0.01);
        results.Single(x => x.Color == Colors.Red).Avg.ShouldBe(6.5, tolerance: 0.01);
    }

    [Fact]
    public async Task group_by_select_key_only()
    {
        await SetupTargetData();

        var results = await _session.Query<Target>()
            .GroupBy(x => x.Color)
            .Select(g => g.Key)
            .ToListAsync();

        results.Count.ShouldBe(3);
        results.ShouldContain(Colors.Blue);
        results.ShouldContain(Colors.Green);
        results.ShouldContain(Colors.Red);
    }

    [Fact]
    public async Task group_by_with_long_count()
    {
        await SetupTargetData();

        var results = await _session.Query<Target>()
            .GroupBy(x => x.Color)
            .Select(g => new { Color = g.Key, Count = g.LongCount() })
            .ToListAsync();

        results.Count.ShouldBe(3);
        results.Single(x => x.Color == Colors.Blue).Count.ShouldBe(2L);
        results.Single(x => x.Color == Colors.Green).Count.ShouldBe(3L);
    }

    // https://github.com/JasperFx/marten/issues/4278
    [Fact]
    public async Task group_by_count()
    {
        await SetupTargetData();

        var count = await _session.Query<Target>()
            .GroupBy(x => x.Color)
            .Select(x => x.Key)      // Select must always follow GroupBy
            .CountAsync();

        // Blue, Green, Red -> three distinct groups
        count.ShouldBe(3);
    }

    // https://github.com/JasperFx/marten/issues/4278
    [Fact]
    public async Task group_by_long_count()
    {
        await SetupTargetData();

        var count = await _session.Query<Target>()
            .GroupBy(x => x.Color)
            .Select(x => x.Key)
            .LongCountAsync();

        count.ShouldBe(3L);
    }
}
