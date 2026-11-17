using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class KeyParent
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public List<KeyChild> Children { get; set; } = new();
}

// A record, so the flattened rows compare by value against LINQ to Objects
public record KeyChild
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
}

public class KeyCustomer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public int Rank { get; set; }
}

public class KeyOrder
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public int Amount { get; set; }
    public string Status { get; set; } = "";
}

// #5968: #5957 made a computed OrderBy() key over the documents translate or be refused. A computed key over the
// child documents a SelectMany() flattens, or over either side of a GroupJoin()/Join(), still went through the
// member lookup, which walks past the computation to the member underneath it: OrderBy(c => -c.Index) and
// OrderBy(r => -r.Amount) silently ordered by c.Index and r.Amount, and arithmetic was refused. Every shape is
// compared, in order, with LINQ to Objects, including paging after the ordering.
public class Bug_5968_computed_ordering_keys_over_children_and_joins: IntegrationContext
{
    // Lower case only, so text ordering agrees under any collation
    private static readonly string[] Pool = ["alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf"];

    private static readonly KeyParent[] Parents = new[] { 3, 1, 5, 2, 4 }.Select(i => new KeyParent
    {
        Id = Guid.NewGuid(),
        Number = i,
        Children = Enumerable.Range(0, 3).Select(c => new KeyChild
        {
            Index = i * 3 + c, Name = Pool[(i * 2 + c) % Pool.Length] + c
        }).ToList()
    }).ToArray();

    private static readonly KeyCustomer[] Customers = Enumerable.Range(1, 4)
        .Select(i => new KeyCustomer { Id = Guid.NewGuid(), Name = Pool[i] + i, Rank = i * 3 % 5 }).ToArray();

    private static readonly KeyOrder[] Orders = Customers.SelectMany((c, i) => Enumerable.Range(0, 3)
        .Select(k => new KeyOrder
        {
            Id = Guid.NewGuid(), CustomerId = c.Id, Amount = (i + 1) * 10 + k, Status = Pool[(i + k) % Pool.Length]
        })).ToArray();

    public Bug_5968_computed_ordering_keys_over_children_and_joins(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override IEnumerable<Type> ClearedBeforeEachTest =>
        [typeof(KeyParent), typeof(KeyCustomer), typeof(KeyOrder)];

    protected override async Task fixtureSetup()
    {
        theSession.Store(Parents);
        theSession.Store(Customers);
        theSession.Store(Orders);
        await theSession.SaveChangesAsync();
    }

    private static async Task matches<T>(IQueryable<T> marten, List<T> expected)
    {
        var sql = marten.ToCommand().CommandText;
        (await marten.ToListAsync()).ShouldBe(expected, sql);
    }

    /// <summary>
    /// The rows in order, a page of them, the first row and the count.
    /// </summary>
    private async Task children<T>(Func<IQueryable<KeyParent>, IQueryable<T>> query)
    {
        var expected = query(Parents.AsQueryable()).ToList();
        await using var session = theStore.QuerySession();

        await matches(query(session.Query<KeyParent>()), expected);
        await matches(query(session.Query<KeyParent>()).Skip(2).Take(4), expected.Skip(2).Take(4).ToList());
        (await query(session.Query<KeyParent>()).FirstAsync()).ShouldBe(expected.First());
        (await query(session.Query<KeyParent>()).CountAsync()).ShouldBe(expected.Count);
    }

    private async Task joined<T>(Func<IQueryable<KeyCustomer>, IQueryable<KeyOrder>, IQueryable<T>> query)
    {
        var expected = query(Customers.AsQueryable(), Orders.AsQueryable()).ToList();
        await using var session = theStore.QuerySession();

        await matches(query(session.Query<KeyCustomer>(), session.Query<KeyOrder>()), expected);
        await matches(query(session.Query<KeyCustomer>(), session.Query<KeyOrder>()).Skip(2).Take(4),
            expected.Skip(2).Take(4).ToList());
        (await query(session.Query<KeyCustomer>(), session.Query<KeyOrder>()).CountAsync()).ShouldBe(expected.Count);
    }

    public static TheoryData<string> ChildShapes =>
    [
        "negation", "negation_descending", "modulo_then_member", "modulo_descending_then_negation", "multiplication",
        "widening_cast", "concatenation", "to_lower_then_member", "length_then_member", "member", "where_then_negation",
        "paged_parents_negation"
    ];

    [Theory]
    [MemberData(nameof(ChildShapes))]
    public Task after_select_many_over_child_documents(string shape) => shape switch
    {
        "negation" => children(q => q.SelectMany(x => x.Children).OrderBy(c => -c.Index)),
        "negation_descending" => children(q => q.SelectMany(x => x.Children).OrderByDescending(c => -c.Index)),
        "modulo_then_member" => children(q =>
            q.SelectMany(x => x.Children).OrderBy(c => c.Index % 3).ThenBy(c => c.Index)),
        "modulo_descending_then_negation" => children(q =>
            q.SelectMany(x => x.Children).OrderByDescending(c => c.Index % 4).ThenBy(c => -c.Index)),
        "multiplication" => children(q => q.SelectMany(x => x.Children).OrderBy(c => c.Index * -2 + 1)),
        "widening_cast" => children(q => q.SelectMany(x => x.Children).OrderBy(c => (long)c.Index * -1)),
        "concatenation" => children(q => q.SelectMany(x => x.Children).OrderBy(c => c.Name + c.Index)),
        "to_lower_then_member" => children(q =>
            q.SelectMany(x => x.Children).OrderBy(c => c.Name.ToLower()).ThenBy(c => c.Index)),
        "length_then_member" => children(q =>
            q.SelectMany(x => x.Children).OrderBy(c => c.Name.Length).ThenByDescending(c => c.Index)),
        "member" => children(q => q.SelectMany(x => x.Children).OrderByDescending(c => c.Index)),
        "where_then_negation" => children(q =>
            q.SelectMany(x => x.Children).Where(c => c.Index % 2 == 0).OrderBy(c => -c.Index)),
        "paged_parents_negation" => children(q =>
            q.OrderBy(x => x.Number).Skip(1).Take(3).SelectMany(x => x.Children).OrderBy(c => -c.Index)),
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    public static TheoryData<string> JoinShapes =>
    [
        "inner_negation", "inner_negation_descending", "inner_modulo_then_member", "outer_negation_then_inner",
        "concatenation_then_member", "group_join_inner_negation", "group_join_outer_arithmetic", "inner_member",
        "outer_member_then_inner"
    ];

    [Theory]
    [MemberData(nameof(JoinShapes))]
    public Task over_joined_rows(string shape) => shape switch
    {
        "inner_negation" => joined((c, o) =>
            c.Join(o, x => x.Id, y => y.CustomerId, (x, y) => new { x.Name, y.Amount }).OrderBy(r => -r.Amount)),
        "inner_negation_descending" => joined((c, o) =>
            c.Join(o, x => x.Id, y => y.CustomerId, (x, y) => new { x.Name, y.Amount })
                .OrderByDescending(r => -r.Amount)),
        "inner_modulo_then_member" => joined((c, o) =>
            c.Join(o, x => x.Id, y => y.CustomerId, (x, y) => new { x.Name, y.Amount })
                .OrderBy(r => r.Amount % 3).ThenBy(r => r.Amount)),
        "outer_negation_then_inner" => joined((c, o) =>
            c.Join(o, x => x.Id, y => y.CustomerId, (x, y) => new { x.Rank, y.Amount })
                .OrderBy(r => -r.Rank).ThenBy(r => r.Amount)),
        "concatenation_then_member" => joined((c, o) =>
            c.Join(o, x => x.Id, y => y.CustomerId, (x, y) => new { x.Name, y.Status, y.Amount })
                .OrderBy(r => r.Status + "-").ThenByDescending(r => r.Amount)),
        "group_join_inner_negation" => joined((c, o) =>
            c.GroupJoin(o, x => x.Id, y => y.CustomerId, (x, ys) => new { x, ys })
                .SelectMany(z => z.ys, (z, y) => new { z.x.Name, y.Amount }).OrderBy(r => -r.Amount)),
        "group_join_outer_arithmetic" => joined((c, o) =>
            c.GroupJoin(o, x => x.Id, y => y.CustomerId, (x, ys) => new { x, ys })
                .SelectMany(z => z.ys, (z, y) => new { z.x.Rank, y.Amount })
                .OrderBy(r => r.Rank * -3 + 1).ThenBy(r => r.Amount)),
        "inner_member" => joined((c, o) =>
            c.Join(o, x => x.Id, y => y.CustomerId, (x, y) => new { x.Name, y.Amount }).OrderByDescending(r => r.Amount)),
        "outer_member_then_inner" => joined((c, o) =>
            c.Join(o, x => x.Id, y => y.CustomerId, (x, y) => new { x.Name, y.Amount })
                .OrderBy(r => r.Name).ThenBy(r => r.Amount)),
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    public static TheoryData<string> RefusedShapes =>
    [
        "child_math_call", "child_conditional", "child_decimal_division", "join_math_call", "join_narrowing"
    ];

    [Theory]
    [MemberData(nameof(RefusedShapes))]
    public async Task untranslatable_keys_are_refused(string shape)
    {
        await using var session = theStore.QuerySession();
        var parents = session.Query<KeyParent>();
        var customers = session.Query<KeyCustomer>();
        var orders = session.Query<KeyOrder>();

        Func<Task> query = shape switch
        {
            "child_math_call" => () => parents.SelectMany(x => x.Children).OrderBy(c => Math.Abs(c.Index - 7))
                .ToListAsync(),
            "child_conditional" => () => parents.SelectMany(x => x.Children).OrderBy(c => c.Index > 5 ? 0 : 1)
                .ToListAsync(),
            "child_decimal_division" => () => parents.SelectMany(x => x.Children).OrderBy(c => (decimal)c.Index / 3m)
                .ToListAsync(),
            "join_math_call" => () => customers
                .Join(orders, x => x.Id, y => y.CustomerId, (x, y) => new { x.Name, y.Amount })
                .OrderBy(r => Math.Abs(r.Amount - 25)).ToListAsync(),
            "join_narrowing" => () => customers
                .Join(orders, x => x.Id, y => y.CustomerId, (x, y) => new { x.Name, y.Amount })
                .OrderBy(r => (short)r.Amount).ToListAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        var ex = await Should.ThrowAsync<BadLinqExpressionException>(query);
        ex.Message.ShouldContain("OrderBy()");
    }

    // Plain member keys send exactly the SQL they did before #5968, captured by executing each query on the #5966
    // branch head (8bd8a2715).
    public static TheoryData<string, string> UnchangedShapes => new()
    {
        { "child_member", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements(CAST(d.data ->> 'Children' as jsonb)) as data from public.mt_doc_keyparent as d) select d.data as data from mt_temp_id_list1CTE as d order by CAST(d.data ->> 'Index' as integer) desc;" },
        { "child_length", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements(CAST(d.data ->> 'Children' as jsonb)) as data from public.mt_doc_keyparent as d) select d.data as data from mt_temp_id_list1CTE as d order by length(d.data ->> 'Name');" },
        { "join_members", "WITH mt_temp_id_list1CTE as (select d.data, d.id from public.mt_doc_keycustomer as d)  , mt_temp_id_list2CTE as (select d.data, CAST(d.data ->> 'CustomerId' as uuid) from public.mt_doc_keyorder as d) select  jsonb_build_object('Name', mt_temp_id_list1CTE.data ->> 'Name', 'Amount', CAST(mt_temp_id_list2CTE.data ->> 'Amount' as integer))  as data from mt_temp_id_list1CTE INNER JOIN mt_temp_id_list2CTE ON mt_temp_id_list1CTE.id = CAST(mt_temp_id_list2CTE.data ->> 'CustomerId' as uuid) order by mt_temp_id_list1CTE.data ->> 'Name', CAST(mt_temp_id_list2CTE.data ->> 'Amount' as integer) desc;" }
    };

    [Theory]
    [MemberData(nameof(UnchangedShapes))]
    public async Task sql_of_plain_member_keys_is_unchanged(string shape, string expected)
    {
        await using var session = theStore.QuerySession();
        var recorder = new ExecutedCommandRecorder();
        session.Logger = recorder;

        _ = shape switch
        {
            "child_member" => (object)await session.Query<KeyParent>().SelectMany(x => x.Children)
                .OrderByDescending(c => c.Index).ToListAsync(),
            "child_length" => await session.Query<KeyParent>().SelectMany(x => x.Children)
                .OrderBy(c => c.Name.Length).ToListAsync(),
            "join_members" => await session.Query<KeyCustomer>()
                .Join(session.Query<KeyOrder>(), x => x.Id, y => y.CustomerId, (x, y) => new { x.Name, y.Amount })
                .OrderBy(r => r.Name).ThenByDescending(r => r.Amount).ToListAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        recorder.Commands.Single().Sql.ShouldBe(expected);
    }
}
