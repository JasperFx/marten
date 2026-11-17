using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class ComputedKeyDoc
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public long Count { get; set; }
    public double Amount { get; set; }
    public decimal Price { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public bool Flag { get; set; }
    public ComputedKeyInner Inner { get; set; } = new();
}

public class ComputedKeyInner
{
    public int Level { get; set; }
    public string Label { get; set; } = "";
}

// #5957: a computed ordering key, OrderBy(x => -x.Number), was replaced by the member underneath it, so the query
// silently ordered by x.Number. Computed keys now translate through the same scalar translation Select() uses
// after SelectMany() (#5933), and a key it cannot translate exactly is refused. Every ordering is compared, in
// order, with LINQ to Objects over the same documents, ascending and descending, with x.Number breaking ties.
public class Bug_5957_computed_ordering_keys: IntegrationContext
{
    private static readonly ComputedKeyDoc[] Docs = Enumerable.Range(1, 9).Select(i => new ComputedKeyDoc
    {
        Id = Guid.NewGuid(),
        Number = i,
        Count = (i * 7) % 10,
        Amount = (i % 4) * 1.25 - i / 10.0,
        Price = i * 1.5m,
        Name = new[] { "pear", "fig", "banana", "kiwi", "apple", "plum", "cherry", "date", "lime" }[i - 1],
        Code = i % 2 == 0 ? "b" : "a",
        Flag = i % 3 == 0,
        Inner = new ComputedKeyInner { Level = (i * 5) % 7, Label = new[] { "x", "yy", "zzz" }[i % 3] }
    }).ToArray();

    public Bug_5957_computed_ordering_keys(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(ComputedKeyDoc)];

    protected override async Task fixtureSetup()
    {
        // One at a time and out of Number order, so an ordering that goes missing shows up
        foreach (var doc in Docs.OrderBy(x => x.Name))
        {
            theSession.Store(doc);
            await theSession.SaveChangesAsync();
        }
    }

    public static IEnumerable<object[]> KeyNames() =>
        from name in ComputedKeys.All().Keys
        from direction in new[] { false, true }
        select new object[] { name, direction };

    [Theory]
    [MemberData(nameof(KeyNames))]
    public async Task ordering_matches_linq_to_objects(string name, bool descending)
    {
        var order = ComputedKeys.All()[name];
        var expected = order(Docs.AsQueryable(), descending).ThenBy(x => x.Number).Select(x => x.Number).ToList();

        await using var session = theStore.QuerySession();
        var query = order(session.Query<ComputedKeyDoc>(), descending).ThenBy(x => x.Number).Select(x => x.Number);

        var actual = await query.ToListAsync();
        actual.ShouldBe(expected, query.ToCommand().CommandText);
    }

    [Fact]
    public async Task negation_is_not_ordered_by_the_member_underneath()
    {
        // The report: this sent `order by CAST(d.data ->> 'Number' as integer)`
        await using var session = theStore.QuerySession();

        var actual = await session.Query<ComputedKeyDoc>().OrderBy(x => -x.Number).Select(x => x.Number).ToListAsync();

        actual.ShouldBe(Docs.Select(x => x.Number).OrderByDescending(x => x).ToList());
    }

    [Fact]
    public async Task then_by_a_computed_key()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => x.Code).ThenByDescending(x => x.Number % 4).ThenBy(x => -x.Inner.Level)
            .ThenBy(x => x.Number).Select(x => x.Number).ToList();
        var actual = await session.Query<ComputedKeyDoc>().OrderBy(x => x.Code).ThenByDescending(x => x.Number % 4)
            .ThenBy(x => -x.Inner.Level).ThenBy(x => x.Number).Select(x => x.Number).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task paging_after_a_computed_key_pages_the_ordered_rows()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => x.Number % 3).ThenByDescending(x => x.Number).Skip(2).Take(4)
            .Select(x => x.Number).ToList();
        var actual = await session.Query<ComputedKeyDoc>().OrderBy(x => x.Number % 3).ThenByDescending(x => x.Number)
            .Skip(2).Take(4).Select(x => x.Number).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task first_after_a_computed_key()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => x.Inner.Level - x.Number).ThenBy(x => x.Number).First();
        var actual = await session.Query<ComputedKeyDoc>().OrderBy(x => x.Inner.Level - x.Number)
            .ThenBy(x => x.Number).FirstAsync();

        actual.Number.ShouldBe(expected.Number);
    }

    [Fact]
    public async Task where_and_a_computed_key()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.Where(x => x.Number > 2).OrderByDescending(x => x.Name.Length).ThenBy(x => x.Name)
            .Select(x => x.Number).ToList();
        var actual = await session.Query<ComputedKeyDoc>().Where(x => x.Number > 2)
            .OrderByDescending(x => x.Name.Length).ThenBy(x => x.Name).Select(x => x.Number).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task a_second_order_by_with_a_computed_key()
    {
        await using var session = theStore.QuerySession();

        var expected = Docs.OrderBy(x => x.Number).OrderBy(x => x.Number % 3).Select(x => x.Number).ToList();
        var actual = await session.Query<ComputedKeyDoc>().OrderBy(x => x.Number).OrderBy(x => x.Number % 3)
            .Select(x => x.Number).ToListAsync();

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task a_computed_key_in_a_compiled_query()
    {
        foreach (var divisor in new[] { 3, 4 })
        {
            var expected = Docs.OrderBy(x => x.Number % divisor).ThenByDescending(x => x.Number)
                .Select(x => x.Number).ToList();

            await using var session = theStore.QuerySession();
            (await session.QueryAsync(new ComputedKeyCompiledQuery { Divisor = divisor })).ToList().ShouldBe(expected);
        }
    }

    public static IEnumerable<object[]> RefusedKeyNames() => RefusedComputedKeys.All().Keys.Select(x => new object[] { x });

    [Theory]
    [MemberData(nameof(RefusedKeyNames))]
    public async Task a_key_without_an_exact_translation_is_refused(string name)
    {
        await using var session = theStore.QuerySession();
        var order = RefusedComputedKeys.All()[name];

        var ex = await Should.ThrowAsync<BadLinqExpressionException>(() =>
            order(session.Query<ComputedKeyDoc>()).ToListAsync());

        ex.Message.ShouldContain("OrderBy() key");
    }

    [Fact]
    public async Task plain_member_keys_keep_their_sql()
    {
        await using var session = theStore.QuerySession();

        session.Query<ComputedKeyDoc>().OrderBy(x => x.Number).ThenByDescending(x => x.Inner.Label)
            .ThenBy(x => x.Name.ToUpper()).ToCommand().CommandText.ShouldEndWith(
                "order by CAST(d.data ->> 'Number' as integer), d.data -> 'Inner' ->> 'Label' desc, upper(d.data ->> 'Name');");
    }
}

public class ComputedKeyCompiledQuery: ICompiledListQuery<ComputedKeyDoc, int>
{
    public int Divisor { get; set; }

    public Expression<Func<IMartenQueryable<ComputedKeyDoc>, IEnumerable<int>>> QueryIs()
    {
        return q => q.OrderBy(x => x.Number % Divisor).ThenByDescending(x => x.Number).Select(x => x.Number);
    }
}

internal static class ComputedKeys
{
    private static Func<IQueryable<ComputedKeyDoc>, bool, IOrderedQueryable<ComputedKeyDoc>> key<TKey>(
        Expression<Func<ComputedKeyDoc, TKey>> key)
    {
        return (q, descending) => descending ? q.OrderByDescending(key) : q.OrderBy(key);
    }

    public static Dictionary<string, Func<IQueryable<ComputedKeyDoc>, bool, IOrderedQueryable<ComputedKeyDoc>>> All()
    {
        var divisor = 4;

        return new()
        {
            ["negate"] = key(x => -x.Number),
            ["negate_nested"] = key(x => -x.Inner.Level),
            ["negate_double"] = key(x => -x.Amount),
            ["modulo"] = key(x => x.Number % 3),
            ["modulo_by_local"] = key(x => x.Number % divisor),
            ["add_members"] = key(x => x.Number + x.Inner.Level),
            ["subtract_members"] = key(x => x.Inner.Level - x.Number),
            ["multiply_by_negative"] = key(x => x.Number * -2),
            ["integer_division"] = key(x => x.Number / 3),
            ["long_and_widened_int"] = key(x => x.Count - x.Number),
            ["double_arithmetic"] = key(x => x.Amount * -1.5),
            ["widening_cast_then_divide"] = key(x => (double)x.Number / 4),
            ["decimal_arithmetic"] = key(x => x.Price - x.Number * 2m),
            ["string_length"] = key(x => x.Name.Length),
            ["nested_string_length"] = key(x => x.Inner.Label.Length),
            ["string_concat"] = key(x => x.Code + x.Name),
            ["to_upper_of_concat"] = key(x => (x.Inner.Label + x.Code).ToUpper()),
            ["int_to_string"] = key(x => x.Inner.Level.ToString())
        };
    }
}

internal static class RefusedComputedKeys
{
    public static Dictionary<string, Func<IQueryable<ComputedKeyDoc>, IQueryable<ComputedKeyDoc>>> All()
    {
        return new()
        {
            ["decimal_division"] = q => q.OrderBy(x => x.Price / 3m),
            ["floating_modulo"] = q => q.OrderBy(x => x.Amount % 2.0),
            ["narrowing_cast"] = q => q.OrderBy(x => (byte)x.Number),
            ["conditional"] = q => q.OrderBy(x => x.Flag ? 1 : 0),
            ["math_call"] = q => q.OrderBy(x => Math.Abs(x.Number - 5)),
            ["substring"] = q => q.ThenByComputed(x => x.Name.Substring(1)),
            ["not"] = q => q.OrderBy(x => !x.Flag),
            ["double_to_string"] = q => q.OrderBy(x => x.Amount.ToString())
        };
    }

    private static IQueryable<ComputedKeyDoc> ThenByComputed(this IQueryable<ComputedKeyDoc> q,
        Expression<Func<ComputedKeyDoc, string>> key) => q.OrderBy(x => x.Number).ThenBy(key);
}
