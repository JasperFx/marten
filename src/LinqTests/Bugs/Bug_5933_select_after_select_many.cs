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

namespace LinqTests.Bugs;

public class FlatParent
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public int[] Values { get; set; } = [];
    public List<string> Names { get; set; } = new();
    public Guid[] Keys { get; set; } = [];
    public FlatColour[] Colours { get; set; } = [];
    public List<FlatChild> Children { get; set; } = new();
}

public enum FlatColour
{
    Red,
    Green,
    Blue
}

// A record, so the flattened rows compare by value against LINQ to Objects
public record FlatChild
{
    public int Parent { get; set; }
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public FlatColour Colour { get; set; }
}

public record FlatDto(int Index, string Name);

public class FlatDtoWithSetters
{
    public int Index { get; set; }
    public string Label { get; set; } = "";

    public override bool Equals(object? obj) => obj is FlatDtoWithSetters o && o.Index == Index && o.Label == Label;
    public override int GetHashCode() => HashCode.Combine(Index, Label);
}

// #5933: a Select() after SelectMany() used to be dropped whenever the select visitor had no translation for
// it -- arithmetic or a method call over a flattened element -- so SelectMany(x => x.Values).Select(v => v % 3)
// returned the raw values. Where(), OrderBy() and Distinct() applied after a projection of child documents were
// dropped too. Every shape here is compared with LINQ to Objects over the same documents, and every shape is
// also counted, tested with Any() and, where the order is defined, asked for First().
public class Bug_5933_select_after_select_many: IntegrationContext
{
    // Lower case only: PostgreSQL orders text by the database's collation, which is a plain byte order on some
    // servers and a culture-aware one on others, and the two only agree on strings of one case
    private static readonly string[] NamePool = ["alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf"];

    // Stored in this order, so the first document in the table has nothing to flatten and the documents are
    // not in Number order. Several documents have empty collections.
    private static readonly int[] StorageOrder = [3, 8, 1, 6, 4, 7, 2, 5];

    private static readonly FlatParent[] Docs = StorageOrder.Select(i => new FlatParent
    {
        Id = Guid.NewGuid(),
        Number = i,
        Values = Enumerable.Range(0, i % 3 * 2).Select(v => i * 10 + v).ToArray(),
        Names = Enumerable.Range(0, i % 4).Select(v => NamePool[(i + v) % NamePool.Length] + v).ToList(),
        Keys = Enumerable.Range(0, i % 3).Select(_ => Guid.NewGuid()).ToArray(),
        Colours = Enumerable.Range(0, i % 3).Select(v => (FlatColour)((i + v) % 3)).ToArray(),
        Children = Enumerable.Range(0, i % 4).Select(c => new FlatChild
        {
            Parent = i, Index = c, Name = NamePool[(i * 2 + c) % NamePool.Length], Colour = (FlatColour)((i + c) % 3)
        }).ToList()
    }).ToArray();

    public Bug_5933_select_after_select_many(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(FlatParent)];

    protected override async Task fixtureSetup()
    {
        // One per call so the physical order follows the array
        foreach (var doc in Docs)
        {
            theSession.Store(doc);
            await theSession.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Fetch, Count(), LongCount() and Any() with Marten and compare each with LINQ to Objects. When the query
    /// defines the order of its rows, the rows are compared in order and First()/FirstOrDefault() too.
    /// </summary>
    private async Task matches<T>(Func<IQueryable<FlatParent>, IQueryable<T>> query, bool ordered)
    {
        await using var session = theStore.QuerySession();

        var expected = query(Docs.AsQueryable()).ToList();
        var marten = query(session.Query<FlatParent>());
        var sql = marten.ToCommand().CommandText;

        var actual = await marten.ToListAsync();
        if (ordered)
        {
            actual.ShouldBe(expected, sql);
        }
        else
        {
            actual.ShouldBe(expected, ignoreOrder: true, customMessage: sql);
        }

        (await query(session.Query<FlatParent>()).CountAsync()).ShouldBe(expected.Count, sql);
        (await query(session.Query<FlatParent>()).LongCountAsync()).ShouldBe(expected.LongCount(), sql);
        (await query(session.Query<FlatParent>()).AnyAsync()).ShouldBe(expected.Any(), sql);

        if (!ordered)
        {
            return;
        }

        (await query(session.Query<FlatParent>()).FirstOrDefaultAsync()).ShouldBe(expected.FirstOrDefault(), sql);

        if (expected.Any())
        {
            (await query(session.Query<FlatParent>()).FirstAsync()).ShouldBe(expected.First(), sql);
        }
        else
        {
            await Should.ThrowAsync<InvalidOperationException>(() => query(session.Query<FlatParent>()).FirstAsync());
        }
    }

    public static TheoryData<string> ScalarCombinations =>
    [
        "plain", "unordered", "where_before", "where_after", "where_before_and_after", "order_by_after",
        "order_by_descending_after", "distinct_after", "take_after", "skip_take_after", "take_between",
        "paged_parents", "paged_parents_where_after", "paged_parents_take_after", "where_after_take",
        "nothing_matches"
    ];

    public static TheoryData<string> ObjectCombinations =>
    [
        "plain", "unordered", "where_before", "take_after", "skip_take_after", "take_between", "paged_parents",
        "paged_parents_take_after"
    ];

    /// <summary>
    /// One scalar projection combined with the operators around it. <paramref name="before" /> filters the
    /// flattened rows and <paramref name="after" /> the projected ones.
    /// </summary>
    private Task scalar<TElement, TResult>(string combination,
        Func<IQueryable<FlatParent>, IQueryable<TElement>> flatten,
        Expression<Func<TElement, TResult>> select,
        Expression<Func<TElement, bool>> before,
        Expression<Func<TResult, bool>> after)
    {
        return combination switch
        {
            "plain" => matches(q => flatten(q.OrderBy(x => x.Number)).Select(select), true),
            "unordered" => matches(q => flatten(q).Select(select), false),
            "where_before" => matches(q => flatten(q.OrderBy(x => x.Number)).Where(before).Select(select), true),
            "where_after" => matches(q => flatten(q.OrderBy(x => x.Number)).Select(select).Where(after), true),
            "where_before_and_after" => matches(
                q => flatten(q.OrderBy(x => x.Number)).Where(before).Select(select).Where(after), true),
            "order_by_after" => matches(q => flatten(q).Select(select).OrderBy(r => r), true),
            "order_by_descending_after" => matches(q => flatten(q).Select(select).OrderByDescending(r => r), true),
            "distinct_after" => matches(q => flatten(q).Select(select).Distinct(), false),
            "take_after" => matches(q => flatten(q.OrderBy(x => x.Number)).Select(select).Take(4), true),
            "skip_take_after" => matches(q => flatten(q.OrderBy(x => x.Number)).Select(select).Skip(2).Take(3), true),
            "take_between" => matches(q => flatten(q.OrderBy(x => x.Number)).Take(5).Select(select).Take(3), true),
            "paged_parents" => matches(
                q => flatten(q.OrderBy(x => x.Number).Skip(2).Take(4)).Select(select), true),
            "paged_parents_where_after" => matches(
                q => flatten(q.OrderBy(x => x.Number).Skip(1).Take(5)).Select(select).Where(after), true),
            "paged_parents_take_after" => matches(
                q => flatten(q.OrderBy(x => x.Number).Skip(1).Take(5)).Select(select).Skip(1).Take(2), true),
            "where_after_take" => matches(q => flatten(q.OrderBy(x => x.Number)).Select(select).Where(after).Take(2),
                true),
            "nothing_matches" => matches(
                q => flatten(q.OrderBy(x => x.Number)).Where(before).Select(select).Where(after).Where(after.Not()),
                true),
            _ => throw new ArgumentOutOfRangeException(nameof(combination))
        };
    }

    /// <summary>
    /// One object projection combined with the operators that can follow it.
    /// </summary>
    private Task projected<TElement, TResult>(string combination,
        Func<IQueryable<FlatParent>, IQueryable<TElement>> flatten,
        Expression<Func<TElement, TResult>> select,
        Expression<Func<TElement, bool>> before)
    {
        return combination switch
        {
            "plain" => matches(q => flatten(q.OrderBy(x => x.Number)).Select(select), true),
            "unordered" => matches(q => flatten(q).Select(select), false),
            "where_before" => matches(q => flatten(q.OrderBy(x => x.Number)).Where(before).Select(select), true),
            "take_after" => matches(q => flatten(q.OrderBy(x => x.Number)).Select(select).Take(4), true),
            "skip_take_after" => matches(q => flatten(q.OrderBy(x => x.Number)).Select(select).Skip(2).Take(3), true),
            "take_between" => matches(q => flatten(q.OrderBy(x => x.Number)).Take(5).Select(select).Take(3), true),
            "paged_parents" => matches(
                q => flatten(q.OrderBy(x => x.Number).Skip(2).Take(4)).Select(select), true),
            "paged_parents_take_after" => matches(
                q => flatten(q.OrderBy(x => x.Number).Skip(1).Take(5)).Select(select).Skip(1).Take(2), true),
            _ => throw new ArgumentOutOfRangeException(nameof(combination))
        };
    }

    #region value collections

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task int_modulo(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Values), v => v % 3, v => v > 25, m => m == 1);

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task int_arithmetic(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Values), v => v * 2 + 1 - v / 4, v => v % 2 == 0, m => m > 50);

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task int_widened_and_negated(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Values), v => -(long)v * 3000000000L, v => v < 70,
            m => m < -100000000000L);

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task int_floating_division(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Values), v => (double)v / 4, v => v != 41, d => d > 10.3);

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task int_to_string(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Values), v => v.ToString(), v => v < 80, s => s.StartsWith("4"));

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task string_to_upper(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Names), s => s.ToUpper(), s => s != "golf0",
            u => u.StartsWith("D") || u.StartsWith("B"));

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task string_to_lower(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Names), s => s.ToLower(), s => s.Contains("o"),
            l => l.EndsWith("1"));

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task string_length(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Names), s => s.Length, s => s != "echo0", l => l > 5);

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task string_concatenation(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Names), s => "<" + s + ">", s => s != "delta1",
            s => s.Contains("a"));

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task guid_to_string(string combination)
    {
        var excluded = Docs[2].Keys[0];
        var wanted = Docs[1].Keys[0].ToString();
        return scalar(combination, q => q.SelectMany(x => x.Keys), g => g.ToString(), g => g != excluded,
            s => s != wanted);
    }

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task enum_to_int(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Colours), c => (int)c, c => c != FlatColour.Red, i => i > 1);

    [Theory]
    [MemberData(nameof(ObjectCombinations))]
    public Task value_anonymous_type(string combination) =>
        projected(combination, q => q.SelectMany(x => x.Values), v => new { V = v, M = v % 3, S = v.ToString() },
            v => v > 25);

    [Theory]
    [MemberData(nameof(ObjectCombinations))]
    public Task value_dto(string combination) =>
        projected(combination, q => q.SelectMany(x => x.Names), s => new FlatDto(s.Length, s.ToUpper()),
            s => s.StartsWith("e") || s.StartsWith("d"));

    [Theory]
    [MemberData(nameof(ObjectCombinations))]
    public Task value_object_initializer(string combination) =>
        projected(combination, q => q.SelectMany(x => x.Values),
            v => new FlatDtoWithSetters { Index = v / 10, Label = "#" + v }, v => v > 25);

    #endregion

    #region child documents

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task child_int_member(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Children), c => c.Index, c => c.Parent != 6, i => i > 0);

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task child_string_member(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Children), c => c.Name, c => c.Index < 2, n => n.Contains("o"));

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task child_arithmetic(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Children), c => c.Index * 10 + c.Parent, c => c.Index > 0,
            n => n % 2 == 1);

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task child_string_method(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Children), c => c.Name.ToUpper(), c => c.Parent > 2,
            n => n.StartsWith("E") || n.EndsWith("O"));

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task child_concatenation_with_int(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Children), c => c.Name + "-" + c.Index, c => c.Parent > 1,
            n => n.EndsWith("1"));

    [Theory]
    [MemberData(nameof(ScalarCombinations))]
    public Task child_enum_as_int(string combination) =>
        scalar(combination, q => q.SelectMany(x => x.Children), c => (int)c.Colour + c.Index, c => c.Index < 2,
            i => i >= 2);

    [Theory]
    [MemberData(nameof(ObjectCombinations))]
    public Task child_anonymous_type(string combination) =>
        projected(combination, q => q.SelectMany(x => x.Children), c => new { c.Index, c.Name }, c => c.Parent != 3);

    [Theory]
    [MemberData(nameof(ObjectCombinations))]
    public Task child_dto(string combination) =>
        projected(combination, q => q.SelectMany(x => x.Children), c => new FlatDto(c.Index, c.Name),
            c => c.Index > 0);

    // Computed members are projected on the client, which is what this shape already did
    [Theory]
    [MemberData(nameof(ObjectCombinations))]
    public Task child_anonymous_type_with_arithmetic(string combination) =>
        projected(combination, q => q.SelectMany(x => x.Children), c => new { c.Index, Doubled = c.Index * 2 },
            c => c.Parent > 4);

    #endregion

    #region other operator shapes

    [Fact]
    public Task order_by_the_element_before_projecting_values() =>
        matches(q => q.SelectMany(x => x.Values).OrderByDescending(v => v).Select(v => v % 3), true);

    [Fact]
    public Task order_by_a_member_before_projecting_children() =>
        matches(q => q.SelectMany(x => x.Children).OrderBy(c => c.Name).ThenBy(c => c.Parent)
            .Select(c => c.Index * 2 + c.Parent), true);

    [Fact]
    public Task distinct_before_projecting() =>
        matches(q => q.SelectMany(x => x.Colours).Distinct().Select(c => (int)c * 10), false);

    [Fact]
    public Task two_projections_in_a_row() =>
        matches(q => q.OrderBy(x => x.Number).SelectMany(x => x.Children).Select(c => c.Index).Select(i => i * 3 + 1),
            true);

    [Fact]
    public Task two_projections_with_operators_between() =>
        matches(q => q.SelectMany(x => x.Values).Select(v => v % 7).Where(m => m > 1).Select(m => m * 10)
            .OrderBy(m => m), true);

    [Fact]
    public Task distinct_then_count_after_projecting()
    {
        return matches(q => q.SelectMany(x => x.Values).Select(v => v % 4).Distinct(), false);
    }

    [Fact]
    public async Task aggregates_of_a_projection()
    {
        await using var session = theStore.QuerySession();

        (await session.Query<FlatParent>().SelectMany(x => x.Values).Select(v => v % 3).SumAsync(m => m))
            .ShouldBe(Docs.SelectMany(x => x.Values).Select(v => v % 3).Sum());

        (await session.Query<FlatParent>().SelectMany(x => x.Values).Select(v => v % 7).MaxAsync(m => m))
            .ShouldBe(Docs.SelectMany(x => x.Values).Select(v => v % 7).Max());

        (await session.Query<FlatParent>().SelectMany(x => x.Children).Select(c => c.Index * 10 + c.Parent).MinAsync(m => m))
            .ShouldBe(Docs.SelectMany(x => x.Children).Select(c => c.Index * 10 + c.Parent).Min());

        (await session.Query<FlatParent>().SelectMany(x => x.Names).Select(s => s.Length).AverageAsync(m => m))
            .ShouldBe(Docs.SelectMany(x => x.Names).Select(s => s.Length).Average(), 0.0000001);
    }

    [Fact]
    public async Task single_after_a_projection()
    {
        await using var session = theStore.QuerySession();

        (await session.Query<FlatParent>().SelectMany(x => x.Values).Select(v => v % 100).Where(m => m == 81)
                .SingleAsync())
            .ShouldBe(81);

        (await session.Query<FlatParent>().SelectMany(x => x.Values).Select(v => v % 100).Where(m => m == 99)
                .SingleOrDefaultAsync())
            .ShouldBe(0);

        await Should.ThrowAsync<InvalidOperationException>(() => session.Query<FlatParent>()
            .SelectMany(x => x.Values).Select(v => v % 2).SingleAsync());
    }

    // Unpaged SelectMany().Select().Any() over rows that only some of the documents -- not the first one stored
    // -- have
    [Fact]
    public async Task any_looks_past_the_first_document()
    {
        await using var session = theStore.QuerySession();

        (await session.Query<FlatParent>().SelectMany(x => x.Values).Select(v => v % 3).AnyAsync()).ShouldBeTrue();
        (await session.Query<FlatParent>().SelectMany(x => x.Children).Select(c => c.Index).AnyAsync()).ShouldBeTrue();
        (await session.Query<FlatParent>().SelectMany(x => x.Values).Select(v => v + 1).Where(m => m == 72).AnyAsync())
            .ShouldBeTrue();
        (await session.Query<FlatParent>().SelectMany(x => x.Values).Select(v => v + 1).Where(m => m == 73).AnyAsync())
            .ShouldBeFalse();
        (await session.Query<FlatParent>().SelectMany(x => x.Names).Select(s => s.ToUpper())
                .AnyAsync(u => u == "FOXTROT0"))
            .ShouldBe(Docs.SelectMany(x => x.Names).Select(s => s.ToUpper()).Any(u => u == "FOXTROT0"));
    }

    [Fact]
    public async Task a_projection_that_reads_a_captured_value()
    {
        await using var session = theStore.QuerySession();

        foreach (var divisor in new[] { 2, 3, 7 })
        {
            (await session.Query<FlatParent>().OrderBy(x => x.Number).SelectMany(x => x.Values)
                    .Select(v => v % divisor).ToListAsync())
                .ShouldBe(Docs.OrderBy(x => x.Number).SelectMany(x => x.Values).Select(v => v % divisor));
        }
    }

    #endregion

    #region refused shapes

    public static TheoryData<string> RefusedShapes =>
    [
        "math_call", "floating_modulo", "decimal_division", "narrowing", "conditional", "enum_to_string",
        "double_to_string", "substring", "whole_child_member_method", "value_anonymous_with_untranslatable_member",
        "child_anonymous_then_where", "child_anonymous_then_order_by", "child_anonymous_then_distinct",
        "child_dto_then_where", "value_anonymous_then_where", "value_anonymous_then_order_by",
        "child_anonymous_then_sum", "group_by_after_projection"
    ];

    [Theory]
    [MemberData(nameof(RefusedShapes))]
    public async Task untranslatable_shapes_are_refused(string shape)
    {
        await using var session = theStore.QuerySession();
        var q = session.Query<FlatParent>();

        Func<Task> query = shape switch
        {
            "math_call" => () => q.SelectMany(x => x.Values).Select(v => Math.Abs(v)).ToListAsync(),
            "floating_modulo" => () => q.SelectMany(x => x.Values).Select(v => (double)v % 2.5).ToListAsync(),
            "decimal_division" => () => q.SelectMany(x => x.Values).Select(v => (decimal)v / 3m).ToListAsync(),
            "narrowing" => () => q.SelectMany(x => x.Values).Select(v => (short)v).ToListAsync(),
            "conditional" => () => q.SelectMany(x => x.Values).Select(v => v > 30 ? 1 : 0).ToListAsync(),
            "enum_to_string" => () => q.SelectMany(x => x.Colours).Select(c => c.ToString()).ToListAsync(),
            "double_to_string" => () => q.SelectMany(x => x.Values).Select(v => ((double)v / 3).ToString()).ToListAsync(),
            "substring" => () => q.SelectMany(x => x.Names).Select(s => s.Substring(1)).ToListAsync(),
            "whole_child_member_method" => () => q.SelectMany(x => x.Children).Select(c => c.Name.Trim()).ToListAsync(),
            "value_anonymous_with_untranslatable_member" => () =>
                q.SelectMany(x => x.Values).Select(v => new { V = v, A = Math.Abs(v) }).ToListAsync(),
            "child_anonymous_then_where" => () =>
                q.SelectMany(x => x.Children).Select(c => new { c.Index, c.Name }).Where(a => a.Index > 0).ToListAsync(),
            "child_anonymous_then_order_by" => () =>
                q.SelectMany(x => x.Children).Select(c => new { c.Index, c.Name }).OrderBy(a => a.Name).ToListAsync(),
            "child_anonymous_then_distinct" => () =>
                q.SelectMany(x => x.Children).Select(c => new { c.Index }).Distinct().ToListAsync(),
            "child_dto_then_where" => () =>
                q.SelectMany(x => x.Children).Select(c => new FlatDto(c.Index, c.Name)).Where(d => d.Index > 0)
                    .ToListAsync(),
            "value_anonymous_then_where" => () =>
                q.SelectMany(x => x.Values).Select(v => new { V = v }).Where(a => a.V > 30).ToListAsync(),
            "value_anonymous_then_order_by" => () =>
                q.SelectMany(x => x.Values).Select(v => new { V = v }).OrderBy(a => a.V).ToListAsync(),
            "child_anonymous_then_sum" => () =>
                q.SelectMany(x => x.Children).Select(c => new { c.Index }).SumAsync(a => a.Index),
            "group_by_after_projection" => () =>
                q.SelectMany(x => x.Values).Select(v => v % 3).GroupBy(m => m).Select(g => new { g.Key, Count = g.Count() })
                    .ToListAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        var ex = await Should.ThrowAsync<BadLinqExpressionException>(query);
        ex.Message.ShouldContain("SelectMany()");
    }

    #endregion

    #region executed SQL

    [Fact]
    public async Task the_projection_runs_over_the_flattened_rows()
    {
        (await executedSql(q => q.SelectMany(x => x.Values).Select(v => v % 3)))
            .ShouldBe("WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Values' as jsonb)) as data from public.mt_doc_flatparent as d)  , " +
                      "mt_temp_id_list2CTE as (select (CAST(d.data as integer) % $1) as data from mt_temp_id_list1CTE as d) " +
                      "select d.data from mt_temp_id_list2CTE as d;");
    }

    [Fact]
    public async Task an_identity_projection_sends_the_sql_of_no_projection()
    {
        (await executedSql(q => q.SelectMany(x => x.Values).Select(v => v).Where(v => v > 3)))
            .ShouldBe(await executedSql(q => q.SelectMany(x => x.Values).Where(v => v > 3)));

        (await executedSql(q => q.OrderBy(x => x.Number).SelectMany(x => x.Children).Select(c => c).Take(2)))
            .ShouldBe(await executedSql(q => q.OrderBy(x => x.Number).SelectMany(x => x.Children).Take(2)));
    }

    // A SelectMany() without a Select() after it sends exactly the SQL it did before #5933, captured by executing
    // each query on the base branch (fix/5928-selectmany-paged-parents at f78d5d12e).
    public static TheoryData<string, string> UnprojectedShapes => new()
    {
        { "values", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Values' as jsonb)) as data from public.mt_doc_flatparent as d) select CAST(data as integer) as data from mt_temp_id_list1CTE as d;" },
        { "values_where_order_by", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Values' as jsonb)) as data from public.mt_doc_flatparent as d)  , mt_temp_id_list2CTE as (select CAST(data as integer) as data from mt_temp_id_list1CTE as d) select CAST(data as integer) as data from mt_temp_id_list2CTE as d where data > $1 order by data desc;" },
        { "values_distinct", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Values' as jsonb)) as data from public.mt_doc_flatparent as d) select DISTINCT(CAST(data as integer)) as data from mt_temp_id_list1CTE as d;" },
        { "names", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Names' as jsonb)) as data from public.mt_doc_flatparent as d) select data as data from mt_temp_id_list1CTE as d;" },
        { "children", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements(CAST(d.data ->> 'Children' as jsonb)) as data from public.mt_doc_flatparent as d) select d.data as data from mt_temp_id_list1CTE as d;" },
        { "children_where_order_by_take", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements(CAST(d.data ->> 'Children' as jsonb)) as data from public.mt_doc_flatparent as d order by CAST(d.data ->> 'Number' as integer)) select d.data as data from mt_temp_id_list1CTE as d where CAST(d.data ->> 'Index' as integer) > $1 order by d.data ->> 'Name' LIMIT $2;" },
        { "children_count", "select SUM(jsonb_array_length(d.data -> 'Children')) from public.mt_doc_flatparent as d;" },
        { "children_any", "select TRUE as result from public.mt_doc_flatparent as d where d.data @? '$ ? (@.Children[*] != null && @.Children[*].size() > 0)' LIMIT $1;" },
        { "paged_children", "WITH mt_temp_id_list1CTE as (select d.* from public.mt_doc_flatparent as d order by CAST(d.data ->> 'Number' as integer) OFFSET $1 LIMIT $2)  , mt_temp_id_list2CTE as (select jsonb_array_elements(CAST(d.data ->> 'Children' as jsonb)) as data from mt_temp_id_list1CTE as d) select d.data as data from mt_temp_id_list2CTE as d;" }
    };

    [Theory]
    [MemberData(nameof(UnprojectedShapes))]
    public async Task unprojected_select_many_sql_is_unchanged(string shape, string expected)
    {
        var actual = shape switch
        {
            "values" => await executedSql(q => q.SelectMany(x => x.Values)),
            "values_where_order_by" => await executedSql(q =>
                q.SelectMany(x => x.Values).Where(v => v > 30).OrderByDescending(v => v)),
            "values_distinct" => await executedSql(q => q.SelectMany(x => x.Values).Distinct()),
            "names" => await executedSql(q => q.SelectMany(x => x.Names)),
            "children" => await executedSql(q => q.SelectMany(x => x.Children)),
            "children_where_order_by_take" => await executedSql(q =>
                q.OrderBy(x => x.Number).SelectMany(x => x.Children).Where(c => c.Index > 0).OrderBy(c => c.Name).Take(3)),
            "children_count" => await executedSql(s => s.Query<FlatParent>().SelectMany(x => x.Children).CountAsync()),
            "children_any" => await executedSql(s => s.Query<FlatParent>().SelectMany(x => x.Children).AnyAsync()),
            "paged_children" => await executedSql(q =>
                q.OrderBy(x => x.Number).Skip(1).Take(3).SelectMany(x => x.Children)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        actual.ShouldBe(expected);
    }

    private async Task<string> executedSql<T>(Func<IQueryable<FlatParent>, IQueryable<T>> shape)
    {
        return await executedSql(session => shape(session.Query<FlatParent>()).ToListAsync());
    }

    private async Task<string> executedSql<T>(Func<IQuerySession, Task<T>> execute)
    {
        await using var session = theStore.QuerySession();
        var recorder = new ExecutedCommandRecorder();
        session.Logger = recorder;

        await execute(session);

        return recorder.Commands.Single().Sql;
    }

    #endregion
}

internal static class PredicateExtensions
{
    public static Expression<Func<T, bool>> Not<T>(this Expression<Func<T, bool>> predicate) =>
        Expression.Lambda<Func<T, bool>>(Expression.Not(predicate.Body), predicate.Parameters);
}
