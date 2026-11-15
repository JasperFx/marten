using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class StringArrayDoc
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string[] Words { get; set; } = [];
    public List<string?> MaybeWords { get; set; } = new();
}

// #5941: a member or method of the flattened string after SelectMany() -- n.Length, n.ToUpper() -- was located as
// a JSON field of the string, CAST(d.data ->> 'Length' as integer), which PostgreSQL refuses with 42883. Every
// shape is compared with LINQ to Objects.
public class Bug_5941_string_members_after_select_many: IntegrationContext
{
    // Lower case only, so PostgreSQL's collation and an ordinal sort agree on the order
    private static readonly string[] Pool = ["alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel"];
    private static readonly string[] MixedPool = ["Alpha", "bravo", "CHARLIE", "Delta", "echo"];

    private static readonly int[] StorageOrder = [3, 8, 1, 6, 4, 7, 2, 5];

    private static readonly StringArrayDoc[] Docs = StorageOrder.Select(i => new StringArrayDoc
    {
        Id = Guid.NewGuid(),
        Number = i,
        Title = MixedPool[i % MixedPool.Length] + i,
        Words = Enumerable.Range(0, i % 4).Select(v => Pool[(i + v * 3) % Pool.Length] + i + v).ToArray(),
        MaybeWords = Enumerable.Range(0, i % 3 + 1)
            .Select(v => (i + v) % 3 == 0 ? null : MixedPool[(i + v) % MixedPool.Length]).ToList()
    }).ToArray();

    public Bug_5941_string_members_after_select_many(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(StringArrayDoc)];

    protected override async Task fixtureSetup()
    {
        foreach (var doc in Docs)
        {
            theSession.Store(doc);
            await theSession.SaveChangesAsync();
        }
    }

    private async Task matches<T>(Func<IQueryable<StringArrayDoc>, IQueryable<T>> query)
    {
        var expected = query(Docs.AsQueryable()).ToList();

        await using var session = theStore.QuerySession();
        var marten = query(session.Query<StringArrayDoc>());
        var sql = marten.ToCommand().CommandText;

        (await marten.ToListAsync()).ShouldBe(expected, sql);
        (await query(session.Query<StringArrayDoc>()).CountAsync()).ShouldBe(expected.Count, sql);
        (await query(session.Query<StringArrayDoc>()).AnyAsync()).ShouldBe(expected.Any(), sql);
        (await query(session.Query<StringArrayDoc>()).FirstOrDefaultAsync()).ShouldBe(expected.FirstOrDefault(), sql);
    }

    public static TheoryData<string> Shapes =>
    [
        "where_length", "where_length_greater", "where_upper", "where_lower", "where_lower_starts_with",
        "where_starts_with", "where_ends_with", "where_contains", "where_equals_ignoring_case", "where_length_and_contains",
        "order_by_length", "order_by_length_descending", "order_by_upper", "select_length", "where_length_select_upper",
        "paged_parents_where_length", "where_length_then_take", "nullable_where_length", "nullable_where_null",
        "nullable_where_upper", "nullable_select_length", "nullable_order_by_length"
    ];

    [Theory]
    [MemberData(nameof(Shapes))]
    public Task matches_linq_to_objects(string shape) => shape switch
    {
        "where_length" => matches(q => q.OrderBy(x => x.Number).SelectMany(x => x.Words).Where(w => w.Length == 6)),
        "where_length_greater" => matches(q =>
            q.OrderBy(x => x.Number).SelectMany(x => x.Words).Where(w => w.Length > 7)),
        "where_upper" => matches(q =>
            q.OrderBy(x => x.Number).SelectMany(x => x.Words).Where(w => w.ToUpper() == "ECHO10")),
        "where_lower" => matches(q =>
            q.OrderBy(x => x.Number).SelectMany(x => x.MaybeWords).Where(w => w != null && w.ToLower() == "charlie")),
        "where_lower_starts_with" => matches(q =>
            q.OrderBy(x => x.Number).SelectMany(x => x.MaybeWords).Where(w => w != null && w.ToLower().StartsWith("d"))),
        "where_starts_with" => matches(q =>
            q.OrderBy(x => x.Number).SelectMany(x => x.Words).Where(w => w.StartsWith("g"))),
        "where_ends_with" => matches(q => q.OrderBy(x => x.Number).SelectMany(x => x.Words).Where(w => w.EndsWith("1"))),
        "where_contains" => matches(q => q.OrderBy(x => x.Number).SelectMany(x => x.Words).Where(w => w.Contains("o"))),
        "where_equals_ignoring_case" => matches(q => q.OrderBy(x => x.Number).SelectMany(x => x.MaybeWords)
            .Where(w => w != null && w.Equals("ALPHA", StringComparison.OrdinalIgnoreCase))),
        "where_length_and_contains" => matches(q => q.OrderBy(x => x.Number).SelectMany(x => x.Words)
            .Where(w => w.Length < 7 && w.Contains("a"))),
        "order_by_length" => matches(q => q.SelectMany(x => x.Words).OrderBy(w => w.Length).ThenBy(w => w)),
        "order_by_length_descending" => matches(q =>
            q.SelectMany(x => x.Words).OrderByDescending(w => w.Length).ThenByDescending(w => w)),
        "order_by_upper" => matches(q => q.SelectMany(x => x.Words).OrderBy(w => w.ToUpper())),
        "select_length" => matches(q => q.OrderBy(x => x.Number).SelectMany(x => x.Words).Select(w => w.Length)),
        "where_length_select_upper" => matches(q => q.OrderBy(x => x.Number).SelectMany(x => x.Words)
            .Where(w => w.Length > 5).Select(w => w.ToUpper())),
        "paged_parents_where_length" => matches(q => q.OrderBy(x => x.Number).Skip(2).Take(4).SelectMany(x => x.Words)
            .Where(w => w.Length <= 6)),
        "where_length_then_take" => matches(q => q.OrderBy(x => x.Number).SelectMany(x => x.Words)
            .Where(w => w.Length > 5).Take(3)),
        "nullable_where_length" => matches(q => q.OrderBy(x => x.Number).SelectMany(x => x.MaybeWords)
            .Where(w => w != null && w.Length == 5)),
        "nullable_where_null" => matches(q => q.OrderBy(x => x.Number).SelectMany(x => x.MaybeWords).Where(w => w == null)),
        "nullable_where_upper" => matches(q => q.OrderBy(x => x.Number).SelectMany(x => x.MaybeWords)
            .Where(w => w != null && w.ToUpper() == "DELTA")),
        "nullable_select_length" => matches(q => q.OrderBy(x => x.Number).SelectMany(x => x.MaybeWords)
            .Where(w => w != null).Select(w => w!.Length)),
        "nullable_order_by_length" => matches(q => q.SelectMany(x => x.MaybeWords).Where(w => w != null)
            .OrderBy(w => w!.Length).ThenBy(w => w!.ToLower())),
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    // The same Length member on a document's own string member, which located a JSON field inside the string too
    [Fact]
    public Task length_of_a_document_member() =>
        matches(q => q.Where(x => x.Title.Length == 6).OrderBy(x => x.Number).Select(x => x.Number));

    // Without Length, a filter on a flattened string sends exactly the SQL it did before #5941, captured by
    // executing each query on the #5936 branch head (6a58e193d).
    public static TheoryData<string, string> UnchangedShapes => new()
    {
        { "starts_with", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Words' as jsonb)) as data from public.mt_doc_stringarraydoc as d)  , mt_temp_id_list2CTE as (select data as data from mt_temp_id_list1CTE as d) select data as data from mt_temp_id_list2CTE as d where data LIKE $1;" },
        { "equals", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Words' as jsonb)) as data from public.mt_doc_stringarraydoc as d)  , mt_temp_id_list2CTE as (select data as data from mt_temp_id_list1CTE as d) select data as data from mt_temp_id_list2CTE as d where data = $1;" },
        { "is_null", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'MaybeWords' as jsonb)) as data from public.mt_doc_stringarraydoc as d)  , mt_temp_id_list2CTE as (select data as data from mt_temp_id_list1CTE as d) select data as data from mt_temp_id_list2CTE as d where data is null;" }
    };

    [Theory]
    [MemberData(nameof(UnchangedShapes))]
    public async Task sql_without_string_members_is_unchanged(string shape, string expected)
    {
        await using var session = theStore.QuerySession();
        var recorder = new ExecutedCommandRecorder();
        session.Logger = recorder;

        _ = shape switch
        {
            "starts_with" => await session.Query<StringArrayDoc>().SelectMany(x => x.Words).Where(w => w.StartsWith("g"))
                .ToListAsync(),
            "equals" => await session.Query<StringArrayDoc>().SelectMany(x => x.Words).Where(w => w == "golf10")
                .ToListAsync(),
            "is_null" => await session.Query<StringArrayDoc>().SelectMany(x => x.MaybeWords).Where(w => w == null)
                .ToListAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        recorder.Commands.Single().Sql.ShouldBe(expected);
    }
}
