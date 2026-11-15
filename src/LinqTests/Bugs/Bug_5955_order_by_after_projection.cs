using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class OrderDoc
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public FlatColour Colour { get; set; }
    public int[] Values { get; set; } = [];
    public List<string> Words { get; set; } = new();
    public OrderDoc? Inner { get; set; }
}

// #5955: two orderings that came back in the wrong order without an error.
//   * OrderBy() over a flattened value collection took any key as the element: OrderBy(v => -v) ordered by v.
//   * OrderBy() after a top-level Select() was dropped (or, for a projection to the document's own type, applied
//     to the documents).
// Every shape is compared, in order, with LINQ to Objects, including paging after the ordering.
public class Bug_5955_order_by_after_projection: IntegrationContext
{
    // Lower case only, so text ordering agrees under any collation
    private static readonly string[] Pool = ["alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel"];

    private static readonly int[] StorageOrder = [3, 8, 1, 6, 4, 7, 2, 5, 10, 9];

    private static readonly OrderDoc[] Docs = StorageOrder.Select(i => new OrderDoc
    {
        Id = Guid.NewGuid(),
        Number = i,
        Name = Pool[i % Pool.Length] + i,
        Colour = (FlatColour)(i % 3),
        Values = Enumerable.Range(0, i % 3 + 1).Select(v => i * 10 + v).ToArray(),
        Words = Enumerable.Range(0, i % 3).Select(v => Pool[(i + v * 3) % Pool.Length] + v + i).ToList(),
        Inner = new OrderDoc { Number = 100 - i, Name = "inner" + i }
    }).ToArray();

    public Bug_5955_order_by_after_projection(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(OrderDoc)];

    protected override async Task fixtureSetup()
    {
        foreach (var doc in Docs)
        {
            theSession.Store(doc);
            await theSession.SaveChangesAsync();
        }
    }

    /// <summary>
    /// The rows in order, then a page of them, Count(), Any() and First()/FirstOrDefault().
    /// </summary>
    private async Task matches<T>(Func<IQueryable<OrderDoc>, IQueryable<T>> query)
    {
        var expected = query(Docs.AsQueryable()).ToList();

        await using var session = theStore.QuerySession();
        var marten = query(session.Query<OrderDoc>());
        var sql = marten.ToCommand().CommandText;

        (await marten.ToListAsync()).ShouldBe(expected, sql);
        (await query(session.Query<OrderDoc>()).Skip(2).Take(3).ToListAsync())
            .ShouldBe(expected.Skip(2).Take(3), sql);
        (await query(session.Query<OrderDoc>()).Take(1).ToListAsync()).ShouldBe(expected.Take(1), sql);
        (await query(session.Query<OrderDoc>()).CountAsync()).ShouldBe(expected.Count, sql);
        (await query(session.Query<OrderDoc>()).AnyAsync()).ShouldBe(expected.Any(), sql);
        (await query(session.Query<OrderDoc>()).FirstOrDefaultAsync()).ShouldBe(expected.FirstOrDefault(), sql);

        if (expected.Any())
        {
            (await query(session.Query<OrderDoc>()).FirstAsync()).ShouldBe(expected.First(), sql);
        }
    }

    public static TheoryData<string> FlattenedShapes =>
    [
        "negation", "negation_descending", "modulo_then_element", "modulo_descending_then_element_descending",
        "arithmetic", "where_then_negation", "paged_parents_negation", "string_length_then_element",
        "string_concatenation", "string_upper", "element", "element_descending"
    ];

    [Theory]
    [MemberData(nameof(FlattenedShapes))]
    public Task after_select_many(string shape) => shape switch
    {
        "negation" => matches(q => q.SelectMany(x => x.Values).OrderBy(v => -v)),
        "negation_descending" => matches(q => q.SelectMany(x => x.Values).OrderByDescending(v => -v)),
        "modulo_then_element" => matches(q => q.SelectMany(x => x.Values).OrderBy(v => v % 3).ThenBy(v => v)),
        "modulo_descending_then_element_descending" => matches(q =>
            q.SelectMany(x => x.Values).OrderByDescending(v => v % 4).ThenByDescending(v => v)),
        "arithmetic" => matches(q => q.SelectMany(x => x.Values).OrderBy(v => v * -2 + 1)),
        "where_then_negation" => matches(q => q.SelectMany(x => x.Values).Where(v => v % 2 == 0).OrderBy(v => -v)),
        "paged_parents_negation" => matches(q =>
            q.OrderBy(x => x.Number).Skip(2).Take(5).SelectMany(x => x.Values).OrderBy(v => -v)),
        "string_length_then_element" => matches(q =>
            q.SelectMany(x => x.Words).OrderBy(w => w.Length).ThenBy(w => w)),
        "string_concatenation" => matches(q => q.SelectMany(x => x.Words).OrderBy(w => "<" + w)),
        "string_upper" => matches(q => q.SelectMany(x => x.Words).OrderByDescending(w => w.ToUpper())),
        "element" => matches(q => q.SelectMany(x => x.Values).OrderBy(v => v)),
        "element_descending" => matches(q => q.SelectMany(x => x.Values).OrderByDescending(v => v)),
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    public static TheoryData<string> ProjectedShapes =>
    [
        "member", "member_descending", "member_negation", "member_modulo_then_member", "string_member",
        "string_member_length_then_member", "enum_member", "computed", "computed_descending", "where_after",
        "distinct", "paged_before", "projection_after_select_many", "same_type_member_scalar"
    ];

    [Theory]
    [MemberData(nameof(ProjectedShapes))]
    public Task after_select(string shape) => shape switch
    {
        "member" => matches(q => q.Select(x => x.Number).OrderBy(n => n)),
        "member_descending" => matches(q => q.Select(x => x.Number).OrderByDescending(n => n)),
        "member_negation" => matches(q => q.Select(x => x.Number).OrderBy(n => -n)),
        "member_modulo_then_member" => matches(q => q.Select(x => x.Number).OrderBy(n => n % 3).ThenBy(n => n)),
        "string_member" => matches(q => q.Select(x => x.Name).OrderByDescending(n => n)),
        "string_member_length_then_member" => matches(q =>
            q.Select(x => x.Name).OrderBy(n => n.Length).ThenBy(n => n)),
        "enum_member" => matches(q => q.Select(x => x.Colour).OrderBy(c => c)),
        "computed" => matches(q => q.Select(x => x.Number * 7 % 11).OrderBy(n => n)),
        "computed_descending" => matches(q => q.Select(x => x.Name + "-" + x.Number).OrderByDescending(n => n)),
        "where_after" => matches(q => q.Select(x => x.Number).Where(n => n > 3).OrderByDescending(n => n)),
        "distinct" => matches(q => q.Select(x => x.Number % 4).Distinct().OrderBy(n => n)),
        "paged_before" => matches(q =>
            q.OrderBy(x => x.Number).Take(6).Select(x => x.Number * 3).OrderByDescending(n => n)),
        "projection_after_select_many" => matches(q =>
            q.SelectMany(x => x.Values).Select(v => v % 7).OrderBy(m => m).ThenByDescending(m => -m)),
        "same_type_member_scalar" => matches(q => q.Select(x => x.Inner!.Number).OrderBy(n => n)),
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    public static TheoryData<string> RefusedShapes =>
    [
        "anonymous_type", "same_type_document", "untranslatable_key"
    ];

    [Theory]
    [MemberData(nameof(RefusedShapes))]
    public async Task unordered_shapes_are_refused(string shape)
    {
        await using var session = theStore.QuerySession();
        var q = session.Query<OrderDoc>();

        Func<Task> query = shape switch
        {
            "anonymous_type" => () => q.Select(x => new { x.Number, x.Name }).OrderBy(a => a.Name).ToListAsync(),
            // Inner is another OrderDoc, so this used to order the documents by their own Number
            "same_type_document" => () => q.Select(x => x.Inner!).OrderBy(i => i.Number).ToListAsync(),
            "untranslatable_key" => () => q.SelectMany(x => x.Values).OrderBy(v => Math.Abs(v - 30)).ToListAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        var ex = await Should.ThrowAsync<BadLinqExpressionException>(query);
        ex.Message.ShouldContain("OrderBy()");
    }

    // Orderings that already worked send exactly the SQL they did before #5955, captured by executing each query
    // on the #5952 branch head (3abb02e7e).
    public static TheoryData<string, string> UnchangedShapes => new()
    {
        { "element", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Values' as jsonb)) as data from public.mt_doc_orderdoc as d)  , mt_temp_id_list2CTE as (select CAST(data as integer) as data from mt_temp_id_list1CTE as d) select CAST(data as integer) as data from mt_temp_id_list2CTE as d order by data desc;" },
        { "string_length", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Words' as jsonb)) as data from public.mt_doc_orderdoc as d)  , mt_temp_id_list2CTE as (select data as data from mt_temp_id_list1CTE as d) select data as data from mt_temp_id_list2CTE as d order by length(data);" },
        { "order_before_select", "select d.data ->> 'Name' from public.mt_doc_orderdoc as d order by CAST(d.data ->> 'Number' as integer);" },
        { "select_take", "select CAST(d.data ->> 'Number' as integer) from public.mt_doc_orderdoc as d LIMIT $1;" },
        { "identity_select", "select d.data from public.mt_doc_orderdoc as d order by CAST(d.data ->> 'Number' as integer);" }
    };

    [Theory]
    [MemberData(nameof(UnchangedShapes))]
    public async Task sql_of_other_orderings_is_unchanged(string shape, string expected)
    {
        await using var session = theStore.QuerySession();
        var recorder = new ExecutedCommandRecorder();
        session.Logger = recorder;
        var q = session.Query<OrderDoc>();

        _ = shape switch
        {
            "element" => (object)await q.SelectMany(x => x.Values).OrderByDescending(v => v).ToListAsync(),
            "string_length" => await q.SelectMany(x => x.Words).OrderBy(w => w.Length).ToListAsync(),
            "order_before_select" => await q.OrderBy(x => x.Number).Select(x => x.Name).ToListAsync(),
            "select_take" => await q.Select(x => x.Number).Take(3).ToListAsync(),
            "identity_select" => await q.Select(x => x).OrderBy(x => x.Number).ToListAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        recorder.Commands.Single().Sql.ShouldBe(expected);
    }
}
