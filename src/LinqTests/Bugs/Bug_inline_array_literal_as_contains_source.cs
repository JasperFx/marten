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
using Weasel.Core;

namespace LinqTests.Bugs;

/// <summary>
///     An array literal written inline as the source of <c>Contains()</c>,
///     <c>new[] { "Red", "Blue" }.Contains(x.Name)</c>, is a <c>NewArrayInit</c> node in the
///     expression tree rather than a constant. The Contains parsers only recognised a constant or a
///     captured member as the value side, so the literal fell through to member resolution and the
///     query failed with "Unable to find any queryable members in expression". The same values in a
///     captured local, or passed to <c>IsOneOf()</c>, have always worked.
/// </summary>
/// <remarks>
///     The instance form binds to <c>Enumerable.Contains</c> under C# 13 (net9.0) and to
///     <c>MemoryExtensions.Contains</c> under C# 14 (net10.0); the static form is
///     <c>Enumerable.Contains</c> on both. Each test therefore covers one parser per target
///     framework, and the static-form test pins the <c>Enumerable</c> parser on net10.0 too.
/// </remarks>
public class Bug_inline_array_literal_as_contains_source: BugIntegrationContext
{
    public enum Shade
    {
        Light,
        Medium,
        Dark
    }

    public class Swatch
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Number { get; set; }
        public Guid Code { get; set; }
        public Shade Shade { get; set; }
    }

    public class SwatchesNamedRedOrBlue: ICompiledListQuery<Swatch>
    {
        public Expression<Func<IMartenQueryable<Swatch>, IEnumerable<Swatch>>> QueryIs()
        {
            return q => q.Where(x => new[] { "Red", "Blue" }.Contains(x.Name));
        }
    }

    public class SwatchesNamedEither: ICompiledListQuery<Swatch>
    {
        public string First { get; set; } = string.Empty;
        public string Second { get; set; } = string.Empty;

        public Expression<Func<IMartenQueryable<Swatch>, IEnumerable<Swatch>>> QueryIs()
        {
            return q => q.Where(x => new[] { First, Second }.Contains(x.Name));
        }
    }

    private readonly Swatch _red = new()
    {
        Id = Guid.NewGuid(),
        Name = "Red",
        Number = 1,
        Code = new Guid("a1a1a1a1-0000-0000-0000-000000000001"),
        Shade = Shade.Light
    };

    private readonly Swatch _green = new()
    {
        Id = Guid.NewGuid(),
        Name = "Green",
        Number = 2,
        Code = new Guid("a1a1a1a1-0000-0000-0000-000000000002"),
        Shade = Shade.Medium
    };

    private readonly Swatch _blue = new()
    {
        Id = Guid.NewGuid(),
        Name = "Blue",
        Number = 3,
        Code = new Guid("a1a1a1a1-0000-0000-0000-000000000003"),
        Shade = Shade.Dark
    };

    private async Task seedAsync(Action<StoreOptions> configure = null)
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Swatch>();
            configure?.Invoke(opts);
        });

        theSession.Store(_red, _green, _blue);
        await theSession.SaveChangesAsync();
    }

    private static void shouldBeRedAndBlue(IEnumerable<Swatch> results)
    {
        results.Select(x => x.Name).OrderBy(x => x).ShouldBe(new[] { "Blue", "Red" });
    }

    [Fact]
    public async Task string_array_literal()
    {
        await seedAsync();

        var results = await theSession.Query<Swatch>()
            .Where(x => new[] { "Red", "Blue" }.Contains(x.Name))
            .ToListAsync();

        shouldBeRedAndBlue(results);
    }

    [Fact]
    public async Task string_array_literal_through_static_enumerable_contains()
    {
        await seedAsync();

        var results = await theSession.Query<Swatch>()
            .Where(x => Enumerable.Contains(new[] { "Red", "Blue" }, x.Name))
            .ToListAsync();

        shouldBeRedAndBlue(results);
    }

    [Fact]
    public async Task negated_string_array_literal()
    {
        await seedAsync();

        var results = await theSession.Query<Swatch>()
            .Where(x => !new[] { "Red", "Blue" }.Contains(x.Name))
            .ToListAsync();

        results.Single().Name.ShouldBe("Green");
    }

    [Fact]
    public async Task int_array_literal()
    {
        await seedAsync();

        var results = await theSession.Query<Swatch>()
            .Where(x => new[] { 1, 3 }.Contains(x.Number))
            .ToListAsync();

        shouldBeRedAndBlue(results);
    }

    [Fact]
    public async Task guid_array_literal()
    {
        await seedAsync();

        // A Guid has no literal form, so each element is a closed `new Guid(...)` expression
        var results = await theSession.Query<Swatch>()
            .Where(x => new[]
            {
                new Guid("a1a1a1a1-0000-0000-0000-000000000001"),
                new Guid("a1a1a1a1-0000-0000-0000-000000000003")
            }.Contains(x.Code))
            .ToListAsync();

        shouldBeRedAndBlue(results);
    }

    [Fact]
    public async Task array_literal_of_captured_values()
    {
        await seedAsync();

        // The array is still written inline, only its elements are captured
        var red = _red.Id;
        var blue = _blue.Id;

        var results = await theSession.Query<Swatch>()
            .Where(x => new[] { red, blue }.Contains(x.Id))
            .ToListAsync();

        shouldBeRedAndBlue(results);
    }

    [Theory]
    [InlineData(EnumStorage.AsInteger)]
    [InlineData(EnumStorage.AsString)]
    public async Task enum_array_literal(EnumStorage enumStorage)
    {
        await seedAsync(opts => opts.UseSystemTextJsonForSerialization(enumStorage: enumStorage));

        var results = await theSession.Query<Swatch>()
            .Where(x => new[] { Shade.Light, Shade.Dark }.Contains(x.Shade))
            .ToListAsync();

        shouldBeRedAndBlue(results);
    }

    [Fact]
    public async Task string_array_literal_against_a_duplicated_field()
    {
        await seedAsync(opts => opts.Schema.For<Swatch>().Duplicate(x => x.Name));

        var query = theSession.Query<Swatch>()
            .Where(x => new[] { "Red", "Blue" }.Contains(x.Name));

        query.ToCommand().CommandText.ShouldContain("d.name");

        shouldBeRedAndBlue(await query.ToListAsync());
    }

    [Fact]
    public async Task compiled_query_with_an_array_literal()
    {
        await seedAsync();

        // The second execution runs from the cached plan
        shouldBeRedAndBlue(await theSession.QueryAsync(new SwatchesNamedRedOrBlue()));
        shouldBeRedAndBlue(await theSession.QueryAsync(new SwatchesNamedRedOrBlue()));
    }

    [Fact]
    public async Task compiled_query_still_refuses_an_array_literal_of_its_own_members()
    {
        await seedAsync();

        // A compiled query plan binds each parameter to one of the query's members by value, and an
        // array assembled from two of them matches neither. Reducing this literal to a constant would
        // bake the planning-time values into every later execution, so it keeps being refused rather
        // than answering the next caller with the first caller's rows.
        await Should.ThrowAsync<BadLinqExpressionException>(() =>
            theSession.QueryAsync(new SwatchesNamedEither { First = "Red", Second = "Blue" }));
    }
}
