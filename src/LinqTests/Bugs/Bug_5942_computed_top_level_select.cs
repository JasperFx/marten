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

public class ComputedDoc
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public long Big { get; set; }
    public string Name { get; set; } = "";
    public Guid Key { get; set; }
    public FlatColour Colour { get; set; }
    public List<int> Values { get; set; } = new();
}

// #5942: a computed top-level Select() -- x.Number % 3, x.Id.ToString() -- failed with an exception from inside
// the expression tree API (ArgumentNullException, "Static method requires null instance"). It is now computed in
// SQL, the same way a projection after SelectMany() is (#5933), or refused with BadLinqExpressionException.
public class Bug_5942_computed_top_level_select: IntegrationContext
{
    // Lower case only, so text ordering agrees under any collation
    private static readonly string[] Pool = ["alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf"];

    private static readonly int[] StorageOrder = [3, 8, 1, 6, 4, 7, 2, 5, 10, 9];

    private static readonly ComputedDoc[] Docs = StorageOrder.Select(i => new ComputedDoc
    {
        Id = Guid.NewGuid(),
        Number = i,
        Big = i * 10_000_000_000L,
        Name = Pool[i % Pool.Length] + i,
        Key = Guid.NewGuid(),
        Colour = (FlatColour)(i % 3),
        Values = Enumerable.Range(0, i % 3).ToList()
    }).ToArray();

    public Bug_5942_computed_top_level_select(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(ComputedDoc)];

    protected override async Task fixtureSetup()
    {
        foreach (var doc in Docs)
        {
            theSession.Store(doc);
            await theSession.SaveChangesAsync();
        }
    }

    private async Task matches<T>(Func<IQueryable<ComputedDoc>, IQueryable<T>> query, bool ordered)
    {
        var expected = query(Docs.AsQueryable()).ToList();

        await using var session = theStore.QuerySession();
        var marten = query(session.Query<ComputedDoc>());
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

        (await query(session.Query<ComputedDoc>()).CountAsync()).ShouldBe(expected.Count, sql);
        (await query(session.Query<ComputedDoc>()).LongCountAsync()).ShouldBe(expected.LongCount(), sql);
        (await query(session.Query<ComputedDoc>()).AnyAsync()).ShouldBe(expected.Any(), sql);

        if (!ordered) return;

        (await query(session.Query<ComputedDoc>()).FirstOrDefaultAsync()).ShouldBe(expected.FirstOrDefault(), sql);
        if (expected.Any())
        {
            (await query(session.Query<ComputedDoc>()).FirstAsync()).ShouldBe(expected.First(), sql);
        }
    }

    public static TheoryData<string> Combinations =>
    [
        "plain", "unordered", "where_before", "where_after", "order_by_after", "order_by_descending_after",
        "distinct", "take_after", "skip_take_before", "skip_take_before_and_after", "where_after_take", "nothing"
    ];

    private Task combination<TResult>(string combination, Expression<Func<ComputedDoc, TResult>> select,
        Expression<Func<TResult, bool>> after)
    {
        return combination switch
        {
            "plain" => matches(q => q.OrderBy(x => x.Number).Select(select), true),
            "unordered" => matches(q => q.Select(select), false),
            "where_before" => matches(q => q.Where(x => x.Number > 3).OrderBy(x => x.Number).Select(select), true),
            "where_after" => matches(q => q.OrderBy(x => x.Number).Select(select).Where(after), true),
            "order_by_after" => matches(q => q.Select(select).OrderBy(r => r), true),
            "order_by_descending_after" => matches(q => q.Select(select).OrderByDescending(r => r), true),
            "distinct" => matches(q => q.Select(select).Distinct(), false),
            "take_after" => matches(q => q.OrderBy(x => x.Number).Select(select).Take(4), true),
            "skip_take_before" => matches(q => q.OrderBy(x => x.Number).Skip(2).Take(5).Select(select), true),
            "skip_take_before_and_after" => matches(
                q => q.OrderBy(x => x.Number).Skip(2).Take(5).Select(select).Skip(1).Take(2), true),
            "where_after_take" => matches(q => q.OrderBy(x => x.Number).Select(select).Where(after).Take(2), true),
            "nothing" => matches(q => q.Where(x => x.Number > 100).OrderBy(x => x.Number).Select(select), true),
            _ => throw new ArgumentOutOfRangeException(nameof(combination))
        };
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public Task modulo(string c) => combination(c, x => x.Number % 3, m => m == 1);

    [Theory]
    [MemberData(nameof(Combinations))]
    public Task arithmetic(string c) => combination(c, x => x.Number * 2 + 1 - x.Number / 4, m => m > 8);

    [Theory]
    [MemberData(nameof(Combinations))]
    public Task negation(string c) => combination(c, x => -x.Number, m => m < -5);

    [Theory]
    [MemberData(nameof(Combinations))]
    public Task floating_division(string c) => combination(c, x => (double)x.Number / 4, d => d > 1.2);

    [Theory]
    [MemberData(nameof(Combinations))]
    public Task long_arithmetic(string c) => combination(c, x => x.Big - x.Number, l => l > 50_000_000_000L);

    // A widening cast failed with NotSupportedException: the member underneath was read as an int
    [Theory]
    [MemberData(nameof(Combinations))]
    public Task widening(string c) => combination(c, x => (long)x.Number * 1_000_000_000L, l => l > 4_000_000_000L);

    [Theory]
    [MemberData(nameof(Combinations))]
    public Task enum_cast(string c) => combination(c, x => (int)x.Colour, i => i > 0);

    [Theory]
    [MemberData(nameof(Combinations))]
    public Task id_to_string(string c) => combination(c, x => x.Id.ToString(), s => s != Docs[0].Id.ToString());

    [Theory]
    [MemberData(nameof(Combinations))]
    public Task guid_member_to_string(string c) => combination(c, x => x.Key.ToString(), s => s.Length == 36);

    [Theory]
    [MemberData(nameof(Combinations))]
    public Task int_to_string(string c) => combination(c, x => x.Number.ToString(), s => s.StartsWith("1"));

    [Theory]
    [MemberData(nameof(Combinations))]
    public Task to_upper(string c) => combination(c, x => x.Name.ToUpper(), s => s.StartsWith("D"));

    [Theory]
    [MemberData(nameof(Combinations))]
    public Task concatenation(string c) => combination(c, x => x.Name + "-" + x.Number, s => s.EndsWith("0"));

    [Theory]
    [MemberData(nameof(Combinations))]
    public Task enum_as_int(string c) => combination(c, x => (int)x.Colour + x.Number, i => i % 2 == 0);

    [Fact]
    public async Task aggregates_of_a_computed_projection()
    {
        await using var session = theStore.QuerySession();

        (await session.Query<ComputedDoc>().SumAsync(x => x.Number % 3)).ShouldBe(Docs.Sum(x => x.Number % 3));
        (await session.Query<ComputedDoc>().MaxAsync(x => x.Number * 7 % 10)).ShouldBe(Docs.Max(x => x.Number * 7 % 10));
        (await session.Query<ComputedDoc>().MinAsync(x => x.Number - 20)).ShouldBe(Docs.Min(x => x.Number - 20));
        (await session.Query<ComputedDoc>().AverageAsync(x => x.Number * 2)).ShouldBe(Docs.Average(x => x.Number * 2),
            0.0000001);
    }

    [Fact]
    public async Task single_of_a_computed_projection()
    {
        await using var session = theStore.QuerySession();

        (await session.Query<ComputedDoc>().Where(x => x.Number == 7).Select(x => x.Number * 3).SingleAsync())
            .ShouldBe(21);
    }

    [Fact]
    public async Task a_captured_value()
    {
        await using var session = theStore.QuerySession();

        foreach (var divisor in new[] { 2, 3, 4 })
        {
            (await session.Query<ComputedDoc>().OrderBy(x => x.Number).Select(x => x.Number % divisor).ToListAsync())
                .ShouldBe(Docs.OrderBy(x => x.Number).Select(x => x.Number % divisor));
        }
    }

    public static TheoryData<string> RefusedShapes =>
    [
        "math_call", "conditional", "substring", "collection_count", "decimal_division", "narrowing", "floating_modulo"
    ];

    [Theory]
    [MemberData(nameof(RefusedShapes))]
    public async Task untranslatable_projections_are_refused(string shape)
    {
        await using var session = theStore.QuerySession();
        var q = session.Query<ComputedDoc>();

        Func<Task> query = shape switch
        {
            "math_call" => () => q.Select(x => Math.Abs(x.Number - 5)).ToListAsync(),
            "conditional" => () => q.Select(x => x.Number > 3 ? 1 : 0).ToListAsync(),
            "substring" => () => q.Select(x => x.Name.Substring(1)).ToListAsync(),
            "collection_count" => () => q.Select(x => x.Values.Count() + 1).ToListAsync(),
            "decimal_division" => () => q.Select(x => (decimal)x.Number / 3m).ToListAsync(),
            "narrowing" => () => q.Select(x => (short)(x.Number + 1)).ToListAsync(),
            "floating_modulo" => () => q.Select(x => (double)x.Number % 2.5).ToListAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        var ex = await Should.ThrowAsync<BadLinqExpressionException>(query);
        ex.Message.ShouldContain("Select()");
    }

    // Projections that were already translated send exactly the SQL they did before #5942, captured by executing
    // each query on the #5944 branch head (2a9d8ab80).
    public static TheoryData<string, string> UnchangedShapes => new()
    {
        { "member", "select CAST(d.data ->> 'Number' as integer) from public.mt_doc_computeddoc as d;" },
        { "string_member", "select d.data ->> 'Name' from public.mt_doc_computeddoc as d;" },
        { "anonymous", "select  (jsonb_build_object('Name', d.data ->> 'Name') || jsonb_strip_nulls(jsonb_build_object('Number', CAST(d.data ->> 'Number' as integer))))  as data from public.mt_doc_computeddoc as d;" },
        { "anonymous_computed", "select d.data as data from public.mt_doc_computeddoc as d;" },
        { "length", "select length(d.data ->> 'Name') from public.mt_doc_computeddoc as d;" }
    };

    [Theory]
    [MemberData(nameof(UnchangedShapes))]
    public async Task sql_of_other_projections_is_unchanged(string shape, string expected)
    {
        await using var session = theStore.QuerySession();
        var recorder = new ExecutedCommandRecorder();
        session.Logger = recorder;
        var q = session.Query<ComputedDoc>();

        _ = shape switch
        {
            "member" => (object)await q.Select(x => x.Number).ToListAsync(),
            "string_member" => await q.Select(x => x.Name).ToListAsync(),
            "anonymous" => await q.Select(x => new { x.Number, x.Name }).ToListAsync(),
            "anonymous_computed" => await q.Select(x => new { Doubled = x.Number * 2 }).ToListAsync(),
            "length" => await q.Select(x => x.Name.Length).ToListAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        recorder.Commands.Single().Sql.ShouldBe(expected);
    }

    [Fact]
    public async Task the_computed_projection_runs_in_sql()
    {
        await using var session = theStore.QuerySession();
        var recorder = new ExecutedCommandRecorder();
        session.Logger = recorder;

        await session.Query<ComputedDoc>().Select(x => x.Number % 3).ToListAsync();

        recorder.Commands.Single().Sql.ShouldBe(
            "WITH mt_temp_id_list1CTE as (select (CAST(d.data ->> 'Number' as integer) % $1) as data from public.mt_doc_computeddoc as d) " +
            "select d.data from mt_temp_id_list1CTE as d;");
    }
}
