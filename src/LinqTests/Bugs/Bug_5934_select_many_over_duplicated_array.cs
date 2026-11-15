using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

// The same document, mapped once with its arrays duplicated into their own columns and once without
public abstract class ArrayDocBase
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public abstract int[] Numbers { get; set; }
    public abstract string[] Tags { get; set; }
    public abstract Guid[] Keys { get; set; }
}

public class DuplicatedArrayDoc: ArrayDocBase
{
    [DuplicateField] public override int[] Numbers { get; set; } = [];
    [DuplicateField] public override string[] Tags { get; set; } = [];
    [DuplicateField] public override Guid[] Keys { get; set; } = [];
}

public class PlainArrayDoc: ArrayDocBase
{
    public override int[] Numbers { get; set; } = [];
    public override string[] Tags { get; set; } = [];
    public override Guid[] Keys { get; set; } = [];
}

// #5934: SelectMany() over a duplicated array field read a CTE it never declared (42P01) and counted with
// jsonb_array_length() of an empty locator (42883). Every shape is compared with LINQ to Objects, and with the
// same query against the same documents mapped WITHOUT the duplicated columns.
public class Bug_5934_select_many_over_duplicated_array: IntegrationContext
{
    // Stored out of Number order; several documents have empty arrays, including the first one stored
    private static readonly int[] StorageOrder = [3, 8, 1, 6, 4, 7, 2, 5];

    private static readonly Guid[][] KeyPool =
        StorageOrder.Select(i => Enumerable.Range(0, i % 3).Select(_ => Guid.NewGuid()).ToArray()).ToArray();

    private static T[] build<T>() where T : ArrayDocBase, new() => StorageOrder.Select((i, position) => new T
    {
        Id = Guid.NewGuid(),
        Number = i,
        Numbers = Enumerable.Range(0, i % 3 * 2).Select(v => i * 10 + v).ToArray(),
        Tags = Enumerable.Range(0, i % 4).Select(v => $"t{i}{v}").ToArray(),
        Keys = KeyPool[position]
    }).ToArray();

    private static readonly DuplicatedArrayDoc[] Duplicated = build<DuplicatedArrayDoc>();
    private static readonly PlainArrayDoc[] Plain = build<PlainArrayDoc>();

    public Bug_5934_select_many_over_duplicated_array(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(DuplicatedArrayDoc), typeof(PlainArrayDoc)];

    protected override async Task fixtureSetup()
    {
        for (var i = 0; i < StorageOrder.Length; i++)
        {
            theSession.Store(Duplicated[i]);
            theSession.Store(Plain[i]);
            await theSession.SaveChangesAsync();
        }
    }

    [Fact]
    public void the_arrays_really_are_duplicated()
    {
        var mapping = theStore.Options.Storage.MappingFor(typeof(DuplicatedArrayDoc));
        mapping.DuplicatedFields.Select(x => x.ColumnName).OrderBy(x => x)
            .ShouldBe(["keys", "numbers", "tags"]);

        theStore.Options.Storage.MappingFor(typeof(PlainArrayDoc)).DuplicatedFields.ShouldBeEmpty();
    }

    /// <summary>
    /// A query shape written once and built over either document type, so the duplicated and the plain mapping
    /// run exactly the same LINQ.
    /// </summary>
    private interface IQueryShape<T>
    {
        IQueryable<T> Apply<TDoc>(IQueryable<TDoc> source) where TDoc : ArrayDocBase;
    }

    /// <summary>
    /// Run one shape against both mappings and LINQ to Objects: the rows, Count(), LongCount(), Any() and, when
    /// the order is defined, First()/FirstOrDefault().
    /// </summary>
    private async Task matches<T>(IQueryShape<T> shape, bool ordered)
    {
        var expected = shape.Apply(Plain.AsQueryable()).ToList();

        await using var session = theStore.QuerySession();

        await check(session.Query<DuplicatedArrayDoc>(), "duplicated");
        await check(session.Query<PlainArrayDoc>(), "plain");

        async Task check<TDoc>(IQueryable<TDoc> source, string mapping) where TDoc : ArrayDocBase
        {
            var sql = $"{mapping}: {shape.Apply(source).ToCommand().CommandText}";

            var actual = await shape.Apply(source).ToListAsync();
            if (ordered)
            {
                actual.ShouldBe(expected, sql);
            }
            else
            {
                actual.ShouldBe(expected, ignoreOrder: true, customMessage: sql);
            }

            (await shape.Apply(source).CountAsync()).ShouldBe(expected.Count, sql);
            (await shape.Apply(source).LongCountAsync()).ShouldBe(expected.LongCount(), sql);
            (await shape.Apply(source).AnyAsync()).ShouldBe(expected.Any(), sql);

            if (!ordered) return;

            (await shape.Apply(source).FirstOrDefaultAsync()).ShouldBe(expected.FirstOrDefault(), sql);
            if (expected.Any())
            {
                (await shape.Apply(source).FirstAsync()).ShouldBe(expected.First(), sql);
            }
        }
    }

    /// <summary>
    /// Re-types <c>x => x.Numbers</c>, written over the base class, to the concrete document type.
    /// </summary>
    private static Expression<Func<TDoc, IEnumerable<TElement>>> over<TDoc, TElement>(
        Expression<Func<ArrayDocBase, IEnumerable<TElement>>> many)
    {
        var parameter = Expression.Parameter(typeof(TDoc), many.Parameters[0].Name);
        var body = new Retype(many.Parameters[0], parameter).Visit(many.Body);
        return Expression.Lambda<Func<TDoc, IEnumerable<TElement>>>(body, parameter);
    }

    private sealed class Retype(ParameterExpression from, ParameterExpression to): ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }

    private sealed class Flattened<TElement>(
        string shape,
        Expression<Func<ArrayDocBase, IEnumerable<TElement>>> many,
        Expression<Func<TElement, bool>> filter): IQueryShape<TElement>
    {
        public IQueryable<TElement> Apply<TDoc>(IQueryable<TDoc> q) where TDoc : ArrayDocBase
        {
            var flatten = over<TDoc, TElement>(many);
            return shape switch
            {
                "plain" => q.OrderBy(x => x.Number).SelectMany(flatten),
                "unordered" => q.SelectMany(flatten),
                "ordered_parents" => q.OrderByDescending(x => x.Number).SelectMany(flatten),
                "where_parents" => q.Where(x => x.Number > 3).OrderBy(x => x.Number).SelectMany(flatten),
                "where_after" => q.OrderBy(x => x.Number).SelectMany(flatten).Where(filter),
                "order_by_after" => q.SelectMany(flatten).OrderByDescending(e => e),
                "distinct" => q.SelectMany(flatten).Distinct(),
                "take_before" => q.OrderBy(x => x.Number).Take(3).SelectMany(flatten),
                "skip_before" => q.OrderBy(x => x.Number).Skip(5).SelectMany(flatten),
                "skip_take_before" => q.OrderBy(x => x.Number).Skip(2).Take(4).SelectMany(flatten),
                "take_after" => q.OrderBy(x => x.Number).SelectMany(flatten).Take(3),
                "skip_take_after" => q.OrderBy(x => x.Number).SelectMany(flatten).Skip(2).Take(3),
                "before_and_after" => q.OrderBy(x => x.Number).Skip(1).Take(5).SelectMany(flatten).Skip(1).Take(3),
                "nothing" => q.Where(x => x.Number > 100).SelectMany(flatten),
                _ => throw new ArgumentOutOfRangeException(nameof(shape))
            };
        }
    }

    private sealed class Projected<TElement, TResult>(
        string shape,
        Expression<Func<ArrayDocBase, IEnumerable<TElement>>> many,
        Expression<Func<TElement, bool>> filter,
        Expression<Func<TElement, TResult>> select): IQueryShape<TResult>
    {
        public IQueryable<TResult> Apply<TDoc>(IQueryable<TDoc> q) where TDoc : ArrayDocBase
        {
            var flatten = over<TDoc, TElement>(many);
            return shape switch
            {
                "select_after" => q.OrderBy(x => x.Number).SelectMany(flatten).Select(select),
                "select_after_where" => q.OrderBy(x => x.Number).SelectMany(flatten).Where(filter).Select(select),
                "paged_select_after" => q.OrderBy(x => x.Number).Skip(1).Take(4).SelectMany(flatten).Select(select)
                    .Take(4),
                _ => throw new ArgumentOutOfRangeException(nameof(shape))
            };
        }
    }

    public static TheoryData<string> Shapes =>
    [
        "plain", "unordered", "ordered_parents", "where_parents", "where_after", "order_by_after", "distinct",
        "take_before", "skip_before", "skip_take_before", "take_after", "skip_take_after", "before_and_after",
        "select_after", "select_after_where", "paged_select_after", "nothing"
    ];

    private Task shape<TElement, TResult>(string shape,
        Expression<Func<ArrayDocBase, IEnumerable<TElement>>> many,
        Expression<Func<TElement, bool>> filter,
        Expression<Func<TElement, TResult>> select)
    {
        if (shape.StartsWith("select_after") || shape == "paged_select_after")
        {
            return matches(new Projected<TElement, TResult>(shape, many, filter, select), true);
        }

        return matches(new Flattened<TElement>(shape, many, filter), shape is not ("unordered" or "distinct"));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public Task int_array(string shape) =>
        this.shape(shape, x => x.Numbers, n => n % 2 == 0, n => n * 3 % 7);

    [Theory]
    [MemberData(nameof(Shapes))]
    public Task string_array(string shape) =>
        this.shape(shape, x => x.Tags, t => t.EndsWith("1"), t => t.ToUpper() + "!");

    [Theory]
    [MemberData(nameof(Shapes))]
    public Task guid_array(string shape)
    {
        var excluded = KeyPool[1][0];
        return this.shape(shape, x => x.Keys, k => k != excluded, k => k.ToString());
    }

    [Theory]
    [InlineData(0, 1)] // Number 1: one tag
    [InlineData(1, 1)] // Number 2: two tags
    [InlineData(3, 1)] // Number 4: no tags
    [InlineData(3, 2)] // Numbers 4 and 5: one tag between them
    public async Task single_or_default(int skip, int take)
    {
        PlainArrayDoc[] ordered = Plain.OrderBy(x => x.Number).ToArray();
        var flattened = ordered.Skip(skip).Take(take).SelectMany(x => x.Tags).ToList();

        await using var session = theStore.QuerySession();
        var query = session.Query<DuplicatedArrayDoc>().OrderBy(x => x.Number).Skip(skip).Take(take)
            .SelectMany(x => x.Tags);

        if (flattened.Count > 1)
        {
            await Should.ThrowAsync<InvalidOperationException>(() => query.SingleOrDefaultAsync());
        }
        else
        {
            (await query.SingleOrDefaultAsync()).ShouldBe(flattened.SingleOrDefault());
        }
    }

    #region executed SQL

    [Fact]
    public async Task flattens_and_counts_the_duplicated_column()
    {
        (await executedSql(s => s.Query<DuplicatedArrayDoc>().SelectMany(x => x.Numbers).ToListAsync()))
            .ShouldBe("WITH mt_temp_id_list1CTE as (select unnest(d.numbers) as data from public.mt_doc_duplicatedarraydoc as d) " +
                      "select CAST(data as integer) as data from mt_temp_id_list1CTE as d;");

        (await executedSql(s => s.Query<DuplicatedArrayDoc>().SelectMany(x => x.Numbers).CountAsync()))
            .ShouldBe("select SUM(coalesce(array_length(d.numbers, 1), 0)) from public.mt_doc_duplicatedarraydoc as d;");

        (await executedSql(s => s.Query<DuplicatedArrayDoc>().OrderBy(x => x.Number).Take(2).SelectMany(x => x.Numbers)
                .CountAsync()))
            .ShouldBe("WITH mt_temp_id_list1CTE as (select d.* from public.mt_doc_duplicatedarraydoc as d " +
                      "order by CAST(d.data ->> 'Number' as integer) LIMIT $1) " +
                      "select SUM(coalesce(array_length(d.numbers, 1), 0)) from mt_temp_id_list1CTE as d;");
    }

    // The same SelectMany() shapes over the non-duplicated mapping send exactly the SQL they did before #5934,
    // captured by executing each query on the #5933 branch head (f7e896902).
    public static TheoryData<string, string> PlainShapes => new()
    {
        { "fetch", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Numbers' as jsonb)) as data from public.mt_doc_plainarraydoc as d) select CAST(data as integer) as data from mt_temp_id_list1CTE as d;" },
        { "count", "select SUM(jsonb_array_length(CAST(d.data ->> 'Numbers' as jsonb))) from public.mt_doc_plainarraydoc as d;" },
        { "paged_count", "WITH mt_temp_id_list1CTE as (select d.data from public.mt_doc_plainarraydoc as d order by CAST(d.data ->> 'Number' as integer) LIMIT $1) select SUM(jsonb_array_length(CAST(d.data ->> 'Numbers' as jsonb))) from mt_temp_id_list1CTE as d;" },
        { "where_after", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Tags' as jsonb)) as data from public.mt_doc_plainarraydoc as d)  , mt_temp_id_list2CTE as (select data as data from mt_temp_id_list1CTE as d) select data as data from mt_temp_id_list2CTE as d where data LIKE $1;" },
        { "any", "select TRUE as result from public.mt_doc_plainarraydoc as d where d.data @? '$ ? (@.Numbers != null && @.Numbers.size() > 0)' LIMIT $1;" }
    };

    [Theory]
    [MemberData(nameof(PlainShapes))]
    public async Task non_duplicated_sql_is_unchanged(string shape, string expected)
    {
        var actual = shape switch
        {
            "fetch" => await executedSql(s => s.Query<PlainArrayDoc>().SelectMany(x => x.Numbers).ToListAsync()),
            "count" => await executedSql(s => s.Query<PlainArrayDoc>().SelectMany(x => x.Numbers).CountAsync()),
            "paged_count" => await executedSql(s =>
                s.Query<PlainArrayDoc>().OrderBy(x => x.Number).Take(2).SelectMany(x => x.Numbers).CountAsync()),
            "where_after" => await executedSql(s =>
                s.Query<PlainArrayDoc>().SelectMany(x => x.Tags).Where(t => t.EndsWith("1")).ToListAsync()),
            "any" => await executedSql(s => s.Query<PlainArrayDoc>().SelectMany(x => x.Numbers).AnyAsync()),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        actual.ShouldBe(expected);
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
