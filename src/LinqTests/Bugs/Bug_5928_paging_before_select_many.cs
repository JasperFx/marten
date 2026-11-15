using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;

namespace LinqTests.Bugs;

public class PagedParent
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public List<PagedChild> Children { get; set; } = new();
    public int[] Values { get; set; } = [];
}

// A record, so the flattened rows compare by value against LINQ to Objects
public record PagedChild
{
    public int Parent { get; set; }
    public int Index { get; set; }
}

// #5928: Take()/Skip() BEFORE a SelectMany() page the parent documents, so the result is every child of the
// parents on that page (LINQ to Objects and EF Core semantics). Marten used to leave the parent's LIMIT/OFFSET
// on the statement that explodes the collection, which paged the flattened rows instead. Paging AFTER the
// SelectMany() still pages the flattened rows. Every fetch is also counted, and the count has to equal the
// number of rows the fetch returned (#5920 made the count follow LINQ semantics first).
public class Bug_5928_paging_before_select_many: IntegrationContext
{
    // 10 parents. Children per parent cycle 1,2,3,0 by Number and values 2,4,0, so several parents have
    // nothing to flatten. They are stored out of Number order, so an ORDER BY that went missing shows up.
    private static readonly int[] StorageOrder = [4, 9, 1, 7, 10, 3, 8, 2, 6, 5];

    private static readonly PagedParent[] Docs = StorageOrder.Select(i => new PagedParent
    {
        Id = Guid.NewGuid(),
        Number = i,
        Children = Enumerable.Range(0, i % 4).Select(c => new PagedChild { Parent = i, Index = c }).ToList(),
        Values = Enumerable.Range(0, i % 3 * 2).Select(v => i * 10 + v).ToArray()
    }).ToArray();

    public Bug_5928_paging_before_select_many(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(PagedParent)];

    protected override async Task fixtureSetup()
    {
        // One per call so the physical order follows the array
        foreach (var doc in Docs)
        {
            theSession.Store(doc);
            await theSession.SaveChangesAsync();
        }
    }

    public static IEnumerable<object[]> Orderings() => [["none"], ["asc"], ["desc"]];

    public static IEnumerable<object[]> TakeValues() =>
        from o in new[] { "none", "asc", "desc" }
        from n in new[] { 0, 1, 3, 10, 25 }
        select new object[] { o, n };

    public static IEnumerable<object[]> SkipValues() =>
        from o in new[] { "none", "asc", "desc" }
        from k in new[] { 0, 1, 4, 10, 15 }
        select new object[] { o, k };

    public static IEnumerable<object[]> SkipTakeValues() =>
        from o in new[] { "none", "asc", "desc" }
        from p in new[] { (0, 3), (4, 3), (8, 5), (10, 3), (2, 25), (3, 0) }
        select new object[] { o, p.Item1, p.Item2 };

    private static IQueryable<PagedParent> ordered(IQueryable<PagedParent> q, string ordering) => ordering switch
    {
        "none" => q,
        "asc" => q.OrderBy(x => x.Number),
        "desc" => q.OrderByDescending(x => x.Number),
        _ => throw new ArgumentOutOfRangeException(nameof(ordering))
    };

    /// <summary>
    /// Fetch <c>flatten(page(...))</c> with Marten and compare it, in order, with LINQ to Objects.
    /// <para>
    /// With an ordering, the reference is LINQ to Objects over the seeded documents. Without one, which parents
    /// are on a page is up to PostgreSQL, so the reference is LINQ to Objects over the page of parents Marten
    /// itself returns for <c>page(...)</c>: the flattened rows must be exactly the children of that page.
    /// </para>
    /// </summary>
    private async Task shouldMatch<T>(string ordering, Func<IQueryable<PagedParent>, IQueryable<PagedParent>> page,
        Func<IQueryable<PagedParent>, IQueryable<T>> flatten)
    {
        await using var session = theStore.QuerySession();

        var parents = ordering == "none"
            ? await page(session.Query<PagedParent>()).ToListAsync()
            : page(ordered(Docs.AsQueryable(), ordering)).ToList();

        var expected = flatten(parents.AsQueryable()).ToList();

        var query = flatten(page(ordered(session.Query<PagedParent>(), ordering)));
        var sql = query.ToCommand().CommandText;

        var actual = await query.ToListAsync();
        actual.ShouldBe(expected, sql);

        var count = await flatten(page(ordered(session.Query<PagedParent>(), ordering))).CountAsync();
        count.ShouldBe(actual.Count, query.ToCommand(FetchType.Count).CommandText);

        var longCount = await flatten(page(ordered(session.Query<PagedParent>(), ordering))).LongCountAsync();
        longCount.ShouldBe(actual.Count);
    }

    #region child documents

    [Theory]
    [MemberData(nameof(TakeValues))]
    public Task take_then_select_many_children(string ordering, int n) =>
        shouldMatch(ordering, q => q.Take(n), q => q.SelectMany(x => x.Children));

    [Theory]
    [MemberData(nameof(SkipValues))]
    public Task skip_then_select_many_children(string ordering, int k) =>
        shouldMatch(ordering, q => q.Skip(k), q => q.SelectMany(x => x.Children));

    [Theory]
    [MemberData(nameof(SkipTakeValues))]
    public Task skip_take_then_select_many_children(string ordering, int k, int n) =>
        shouldMatch(ordering, q => q.Skip(k).Take(n), q => q.SelectMany(x => x.Children));

    #endregion

    #region value collections

    [Theory]
    [MemberData(nameof(TakeValues))]
    public Task take_then_select_many_values(string ordering, int n) =>
        shouldMatch(ordering, q => q.Take(n), q => q.SelectMany(x => x.Values));

    [Theory]
    [MemberData(nameof(SkipValues))]
    public Task skip_then_select_many_values(string ordering, int k) =>
        shouldMatch(ordering, q => q.Skip(k), q => q.SelectMany(x => x.Values));

    [Theory]
    [MemberData(nameof(SkipTakeValues))]
    public Task skip_take_then_select_many_values(string ordering, int k, int n) =>
        shouldMatch(ordering, q => q.Skip(k).Take(n), q => q.SelectMany(x => x.Values));

    #endregion

    #region paging before and after

    [Theory]
    [MemberData(nameof(Orderings))]
    public Task take_select_many_take(string ordering) =>
        shouldMatch(ordering, q => q.Take(3), q => q.SelectMany(x => x.Children).Take(2));

    [Theory]
    [MemberData(nameof(Orderings))]
    public Task skip_take_select_many_skip(string ordering) =>
        shouldMatch(ordering, q => q.Skip(1).Take(3), q => q.SelectMany(x => x.Children).Skip(1));

    [Theory]
    [MemberData(nameof(Orderings))]
    public Task skip_take_select_many_skip_take(string ordering) =>
        shouldMatch(ordering, q => q.Skip(1).Take(5), q => q.SelectMany(x => x.Children).Skip(1).Take(3));

    [Theory]
    [MemberData(nameof(Orderings))]
    public Task take_select_many_values_take(string ordering) =>
        shouldMatch(ordering, q => q.Take(3), q => q.SelectMany(x => x.Values).Take(2));

    [Theory]
    [MemberData(nameof(Orderings))]
    public Task skip_take_select_many_values_skip(string ordering) =>
        shouldMatch(ordering, q => q.Skip(1).Take(3), q => q.SelectMany(x => x.Values).Skip(1));

    // Paging only after the SelectMany() already paged the flattened rows; it has to keep doing so
    [Theory]
    [MemberData(nameof(Orderings))]
    public Task select_many_then_take(string ordering) =>
        shouldMatch(ordering, q => q, q => q.SelectMany(x => x.Children).Take(4));

    [Theory]
    [MemberData(nameof(Orderings))]
    public Task select_many_then_skip_take(string ordering) =>
        shouldMatch(ordering, q => q, q => q.SelectMany(x => x.Values).Skip(2).Take(3));

    #endregion

    #region other operators around the paging

    [Theory]
    [MemberData(nameof(SkipTakeValues))]
    public Task where_before_the_paging(string ordering, int k, int n) =>
        shouldMatch(ordering, q => q.Where(x => x.Number > 2).Skip(k).Take(n), q => q.SelectMany(x => x.Children));

    [Theory]
    [MemberData(nameof(Orderings))]
    public Task where_after_the_select_many(string ordering) =>
        shouldMatch(ordering, q => q.Skip(1).Take(5), q => q.SelectMany(x => x.Children).Where(c => c.Index > 0));

    [Theory]
    [MemberData(nameof(Orderings))]
    public Task where_after_the_select_many_values(string ordering) =>
        shouldMatch(ordering, q => q.Skip(1).Take(5), q => q.SelectMany(x => x.Values).Where(v => v % 2 == 0));

    [Theory]
    [MemberData(nameof(Orderings))]
    public Task order_by_after_the_select_many(string ordering) =>
        shouldMatch(ordering, q => q.Take(5),
            q => q.SelectMany(x => x.Children).OrderByDescending(c => c.Index).ThenBy(c => c.Parent));

    [Theory]
    [MemberData(nameof(Orderings))]
    public Task select_after_the_select_many(string ordering) =>
        shouldMatch(ordering, q => q.Skip(2).Take(4), q => q.SelectMany(x => x.Children).Select(c => c.Index));

    [Theory]
    [MemberData(nameof(Orderings))]
    public async Task distinct_after_the_select_many(string ordering)
    {
        // Distinct() has no defined order in SQL, so compare as sets
        await using var session = theStore.QuerySession();

        var parents = ordering == "none"
            ? await session.Query<PagedParent>().Take(5).ToListAsync()
            : ordered(Docs.AsQueryable(), ordering).Take(5).ToList();

        var expected = parents.SelectMany(x => x.Children).Select(c => c.Index).Distinct().OrderBy(x => x).ToList();

        var actual = await ordered(session.Query<PagedParent>(), ordering).Take(5)
            .SelectMany(x => x.Children).Select(c => c.Index).Distinct().ToListAsync();

        actual.OrderBy(x => x).ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(SkipTakeValues))]
    public async Task any_after_paged_select_many(string ordering, int k, int n)
    {
        if (ordering == "none") return; // which parents are on an unordered page is up to PostgreSQL

        await using var session = theStore.QuerySession();

        var expected = ordered(Docs.AsQueryable(), ordering).Skip(k).Take(n).SelectMany(x => x.Children).Any();
        (await ordered(session.Query<PagedParent>(), ordering).Skip(k).Take(n).SelectMany(x => x.Children).AnyAsync())
            .ShouldBe(expected);

        var expectedValues = ordered(Docs.AsQueryable(), ordering).Skip(k).Take(n).SelectMany(x => x.Values).Any();
        (await ordered(session.Query<PagedParent>(), ordering).Skip(k).Take(n).SelectMany(x => x.Values).AnyAsync())
            .ShouldBe(expectedValues);
    }

    // Without paging, Any() over a SelectMany() asks every parent. The parent that comes first in each ordering
    // has nothing to flatten, so asking only the first one would answer false.
    [Theory]
    [MemberData(nameof(Orderings))]
    public async Task any_without_paging_asks_every_parent(string ordering)
    {
        await using var session = theStore.QuerySession();

        // Parents 4 and 8 have no children, 7 has three. Parents 3, 6 and 9 have no values, 7 has two.
        var childQuery = ordered(session.Query<PagedParent>().Where(x => x.Number == 4 || x.Number == 8 || x.Number == 7), ordering);
        (await childQuery.SelectMany(x => x.Children).AnyAsync()).ShouldBeTrue();

        var valueQuery = ordered(session.Query<PagedParent>().Where(x => x.Number == 3 || x.Number == 6 || x.Number == 9 || x.Number == 7), ordering);
        (await valueQuery.SelectMany(x => x.Values).AnyAsync()).ShouldBeTrue();

        (await session.Query<PagedParent>().SelectMany(x => x.Children).AnyAsync()).ShouldBeTrue();
        (await session.Query<PagedParent>().Where(x => x.Number == 4 || x.Number == 8).SelectMany(x => x.Children)
            .AnyAsync()).ShouldBeFalse();
    }

    #endregion

    #region First / Single

    [Theory]
    [MemberData(nameof(SkipTakeValues))]
    public async Task first_or_default_after_paged_select_many(string ordering, int k, int n)
    {
        if (ordering == "none") return;

        await using var session = theStore.QuerySession();

        var expected = ordered(Docs.AsQueryable(), ordering).Skip(k).Take(n).SelectMany(x => x.Children)
            .FirstOrDefault();
        var actual = await ordered(session.Query<PagedParent>(), ordering).Skip(k).Take(n)
            .SelectMany(x => x.Children).FirstOrDefaultAsync();

        actual.ShouldBe(expected);

        var expectedValue = ordered(Docs.AsQueryable(), ordering).Skip(k).Take(n).SelectMany(x => x.Values)
            .FirstOrDefault();
        var actualValue = await ordered(session.Query<PagedParent>(), ordering).Skip(k).Take(n)
            .SelectMany(x => x.Values).FirstOrDefaultAsync();

        actualValue.ShouldBe(expectedValue);
    }

    [Theory]
    [InlineData(0, 1)] // parent 1: one child
    [InlineData(1, 1)] // parent 2: two children
    [InlineData(3, 1)] // parent 4: no children
    [InlineData(3, 2)] // parents 4 and 5: one child between them
    [InlineData(2, 3)] // parents 3, 4, 5: four children
    public async Task single_or_default_after_paged_select_many(int k, int n)
    {
        await using var session = theStore.QuerySession();

        PagedChild? expected = null;
        Exception? expectedException = null;
        try
        {
            expected = Docs.OrderBy(x => x.Number).Skip(k).Take(n).SelectMany(x => x.Children).SingleOrDefault();
        }
        catch (Exception e)
        {
            expectedException = e;
        }

        var query = session.Query<PagedParent>().OrderBy(x => x.Number).Skip(k).Take(n).SelectMany(x => x.Children);

        if (expectedException != null)
        {
            await Should.ThrowAsync<InvalidOperationException>(() => query.SingleOrDefaultAsync());
        }
        else
        {
            (await query.SingleOrDefaultAsync()).ShouldBe(expected);
        }
    }

    #endregion

    #region executed SQL

    [Fact]
    public async Task paged_parents_are_their_own_cte()
    {
        (await executedSql(q => q.OrderBy(x => x.Number).Skip(1).Take(3).SelectMany(x => x.Children)))
            .ShouldBe("WITH mt_temp_id_list1CTE as (select d.* from public.mt_doc_pagedparent as d " +
                      "order by CAST(d.data ->> 'Number' as integer) OFFSET $1 LIMIT $2)  , " +
                      "mt_temp_id_list2CTE as (select jsonb_array_elements(CAST(d.data ->> 'Children' as jsonb)) as data " +
                      "from mt_temp_id_list1CTE as d) " +
                      "select d.data as data from mt_temp_id_list2CTE as d;");
    }

    // Any() asks the page of parents whether one of them has something to flatten
    [Fact]
    public async Task paged_any_asks_the_page()
    {
        (await executedSql(session => session.Query<PagedParent>().OrderBy(x => x.Number).Skip(1).Take(3)
                .SelectMany(x => x.Children).AnyAsync()))
            .ShouldBe("WITH mt_temp_id_list1CTE as (select d.* from public.mt_doc_pagedparent as d " +
                      "order by CAST(d.data ->> 'Number' as integer) OFFSET $1 LIMIT $2) " +
                      "select TRUE as result from mt_temp_id_list1CTE as d " +
                      "where d.data @? '$ ? (@.Children[*] != null && @.Children[*].size() > 0)' LIMIT $3;");
    }

    // First() (#5921) limits the flattened rows, after the page of parents has been exploded
    [Fact]
    public async Task paged_first_limits_the_flattened_rows()
    {
        (await executedSql(session => session.Query<PagedParent>().OrderBy(x => x.Number).Skip(1).Take(3)
                .SelectMany(x => x.Children).FirstOrDefaultAsync()))
            .ShouldBe("WITH mt_temp_id_list1CTE as (select d.* from public.mt_doc_pagedparent as d " +
                      "order by CAST(d.data ->> 'Number' as integer) OFFSET $1 LIMIT $2)  , " +
                      "mt_temp_id_list2CTE as (select jsonb_array_elements(CAST(d.data ->> 'Children' as jsonb)) as data " +
                      "from mt_temp_id_list1CTE as d) " +
                      "select d.data as data from mt_temp_id_list2CTE as d LIMIT $3;");
    }

    // A SelectMany() whose parents are not paged sends exactly the SQL it did before #5928, captured by executing
    // each query on master at 5bbd73207.
    public static IEnumerable<object[]> UnpagedShapes() =>
    [
        ["children", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements(CAST(d.data ->> 'Children' as jsonb)) as data from public.mt_doc_pagedparent as d) select d.data as data from mt_temp_id_list1CTE as d;"],
        ["ordered_children", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements(CAST(d.data ->> 'Children' as jsonb)) as data from public.mt_doc_pagedparent as d order by CAST(d.data ->> 'Number' as integer)) select d.data as data from mt_temp_id_list1CTE as d;"],
        ["values", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Values' as jsonb)) as data from public.mt_doc_pagedparent as d) select CAST(data as integer) as data from mt_temp_id_list1CTE as d;"],
        ["children_then_take", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements(CAST(d.data ->> 'Children' as jsonb)) as data from public.mt_doc_pagedparent as d order by CAST(d.data ->> 'Number' as integer)) select d.data as data from mt_temp_id_list1CTE as d OFFSET $1 LIMIT $2;"],
        ["children_where_order_by", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements(CAST(d.data ->> 'Children' as jsonb)) as data from public.mt_doc_pagedparent as d) select d.data as data from mt_temp_id_list1CTE as d where CAST(d.data ->> 'Index' as integer) > $1 order by CAST(d.data ->> 'Parent' as integer);"],
        ["first_child", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements(CAST(d.data ->> 'Children' as jsonb)) as data from public.mt_doc_pagedparent as d order by CAST(d.data ->> 'Number' as integer)) select d.data as data from mt_temp_id_list1CTE as d LIMIT $1;"],
        ["any_children", "select TRUE as result from public.mt_doc_pagedparent as d where d.data @? '$ ? (@.Children[*] != null && @.Children[*].size() > 0)' LIMIT $1;"],
        ["ordered_any_values", "select TRUE as result from public.mt_doc_pagedparent as d where d.data @? '$ ? (@.Values != null && @.Values.size() > 0)' order by CAST(d.data ->> 'Number' as integer) LIMIT $1;"],
        ["where_then_values", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Values' as jsonb)) as data from public.mt_doc_pagedparent as d where CAST(d.data ->> 'Number' as integer) > $1) select CAST(data as integer) as data from mt_temp_id_list1CTE as d;"]
    ];

    [Theory]
    [MemberData(nameof(UnpagedShapes))]
    public async Task unpaged_select_many_sql_is_unchanged(string shape, string expected)
    {
        var actual = shape switch
        {
            "children" => await executedSql(q => q.SelectMany(x => x.Children)),
            "ordered_children" => await executedSql(q => q.OrderBy(x => x.Number).SelectMany(x => x.Children)),
            "values" => await executedSql(q => q.SelectMany(x => x.Values)),
            "children_then_take" => await executedSql(q => q.OrderBy(x => x.Number).SelectMany(x => x.Children).Skip(1).Take(2)),
            "children_where_order_by" => await executedSql(q =>
                q.SelectMany(x => x.Children).Where(c => c.Index > 0).OrderBy(c => c.Parent)),
            "first_child" => await executedFirstSql(),
            "where_then_values" => await executedSql(q => q.Where(x => x.Number > 2).SelectMany(x => x.Values)),
            "any_children" => await executedSql(session => session.Query<PagedParent>().SelectMany(x => x.Children).AnyAsync()),
            "ordered_any_values" => await executedSql(session =>
                session.Query<PagedParent>().OrderBy(x => x.Number).SelectMany(x => x.Values).AnyAsync()),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        actual.ShouldBe(expected);
    }

    private async Task<string> executedSql<T>(Func<IQueryable<PagedParent>, IQueryable<T>> shape)
    {
        await using var session = theStore.QuerySession();
        var recorder = new CommandRecorder();
        session.Logger = recorder;

        await shape(session.Query<PagedParent>()).ToListAsync();

        return recorder.Commands.Single();
    }

    private async Task<string> executedSql<T>(Func<IQuerySession, Task<T>> execute)
    {
        await using var session = theStore.QuerySession();
        var recorder = new CommandRecorder();
        session.Logger = recorder;

        await execute(session);

        return recorder.Commands.Single();
    }

    private async Task<string> executedFirstSql()
    {
        await using var session = theStore.QuerySession();
        var recorder = new CommandRecorder();
        session.Logger = recorder;

        await session.Query<PagedParent>().OrderBy(x => x.Number).SelectMany(x => x.Children).FirstOrDefaultAsync();

        return recorder.Commands.Single();
    }

    // Records the text of every command the session actually sends
    private sealed class CommandRecorder: IMartenSessionLogger
    {
        public List<string> Commands { get; } = new();

        public void LogSuccess(NpgsqlCommand command) => Commands.Add(command.CommandText);

        public void LogSuccess(NpgsqlBatch batch) =>
            Commands.AddRange(batch.BatchCommands.Cast<NpgsqlBatchCommand>().Select(x => x.CommandText));

        public void LogFailure(NpgsqlCommand command, Exception ex) { }
        public void LogFailure(NpgsqlBatch batch, Exception ex) { }
        public void LogFailure(Exception ex, string message) { }
        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit) { }
        public void OnBeforeExecute(NpgsqlCommand command) { }
        public void OnBeforeExecute(NpgsqlBatch batch) { }
    }

    #endregion
}

public class PagedDuplicatedParent
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public int[] Values { get; set; } = [];
}

// #5928: Any() over a SelectMany() of a DUPLICATED array field filters on the field's own column (d.values), so the
// CTE of a paged parent statement has to carry that column, not just d.data.
public class Bug_5928_paging_before_select_many_duplicated_array: OneOffConfigurationsContext
{
    // Values per parent cycle 2,4,0 by Number, stored out of Number order
    private static readonly PagedDuplicatedParent[] Docs = new[] { 4, 9, 1, 7, 10, 3, 8, 2, 6, 5 }.Select(i =>
        new PagedDuplicatedParent
        {
            Id = Guid.NewGuid(), Number = i, Values = Enumerable.Range(0, i % 3 * 2).Select(v => i * 10 + v).ToArray()
        }).ToArray();

    private async Task seed()
    {
        StoreOptions(opts => opts.Schema.For<PagedDuplicatedParent>().Duplicate(x => x.Values));
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(PagedDuplicatedParent));

        theSession.Store(Docs);
        await theSession.SaveChangesAsync();
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(2, 1)] // parent 3: no values
    [InlineData(5, 1)] // parent 6: no values
    [InlineData(4, 3)]
    [InlineData(8, 5)]
    [InlineData(10, 3)]
    [InlineData(3, 0)]
    public async Task paged_any(int k, int n)
    {
        await seed();

        var expected = Docs.OrderBy(x => x.Number).Skip(k).Take(n).SelectMany(x => x.Values).Any();

        (await theSession.Query<PagedDuplicatedParent>().OrderBy(x => x.Number).Skip(k).Take(n)
            .SelectMany(x => x.Values).AnyAsync()).ShouldBe(expected);
    }

    [Fact]
    public async Task unpaged_any()
    {
        await seed();

        (await theSession.Query<PagedDuplicatedParent>().SelectMany(x => x.Values).AnyAsync()).ShouldBeTrue();
        (await theSession.Query<PagedDuplicatedParent>().Where(x => x.Number == 3 || x.Number == 6)
            .SelectMany(x => x.Values).AnyAsync()).ShouldBeFalse();
    }
}
