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

public class PagedCountDoc
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Color { get; set; } = "";
    public List<PagedCountChild> Children { get; set; } = new();
    public int[] Tags { get; set; } = [];
}

public class PagedCountChild
{
    public int Value { get; set; }
}

// #5920: Count()/LongCount() after Take()/Skip() used to put the LIMIT/OFFSET on the count(*) statement itself,
// so Take(n).Count() returned the full count and Skip(k).Count() returned 0 (the OFFSET skipped the only row
// the aggregate returns). Every shape here is counted by Marten and by LINQ to Objects over the same seeded
// documents, so the expected value is whatever the paged sequence actually holds.
public class Bug_5920_count_over_paged_queries: IntegrationContext
{
    private static readonly string[] Colors = ["Red", "Green", "Blue"];

    // 10 documents. Children per document cycle 0,1,2,3 -> 13 children in all; tags give 6 distinct values.
    private static readonly PagedCountDoc[] Docs = Enumerable.Range(1, 10).Select(i => new PagedCountDoc
    {
        Id = Guid.NewGuid(),
        Number = i,
        Color = Colors[i % 3],
        Children = Enumerable.Range(1, i % 4).Select(c => new PagedCountChild { Value = c }).ToList(),
        Tags = [i % 6, (i + 1) % 6]
    }).ToArray();

    public Bug_5920_count_over_paged_queries(DefaultStoreFixture fixture): base(fixture)
    {
    }

    protected override IEnumerable<Type> ClearedBeforeEachTest => [typeof(PagedCountDoc)];

    protected override async Task fixtureSetup()
    {
        theSession.Store(Docs);
        await theSession.SaveChangesAsync();
    }

    private async Task countsShouldMatch<T>(Func<IQueryable<PagedCountDoc>, IQueryable<T>> shape)
    {
        var expected = shape(Docs.AsQueryable()).Count();

        await using var session = theStore.QuerySession();

        var count = await shape(session.Query<PagedCountDoc>()).CountAsync();
        count.ShouldBe(expected, shape(session.Query<PagedCountDoc>()).ToCommand(FetchType.Count).CommandText);

        var longCount = await shape(session.Query<PagedCountDoc>()).LongCountAsync();
        longCount.ShouldBe(expected);
    }

    public static IEnumerable<object[]> TakeValues() => [[0], [1], [3], [10], [25]];

    public static IEnumerable<object[]> SkipValues() => [[0], [1], [4], [10], [15]];

    public static IEnumerable<object[]> SkipTakeValues() =>
        [[0, 3], [4, 3], [8, 5], [10, 3], [15, 2], [2, 25], [3, 0]];

    [Theory]
    [MemberData(nameof(TakeValues))]
    public Task take(int n) => countsShouldMatch(q => q.Take(n));

    [Theory]
    [MemberData(nameof(SkipValues))]
    public Task skip(int k) => countsShouldMatch(q => q.Skip(k));

    [Theory]
    [MemberData(nameof(SkipTakeValues))]
    public Task skip_then_take(int k, int n) => countsShouldMatch(q => q.Skip(k).Take(n));

    [Theory]
    [MemberData(nameof(SkipTakeValues))]
    public Task order_by_skip_take(int k, int n) =>
        countsShouldMatch(q => q.OrderBy(x => x.Number).Skip(k).Take(n));

    [Theory]
    [MemberData(nameof(SkipTakeValues))]
    public Task order_by_descending_skip_take(int k, int n) =>
        countsShouldMatch(q => q.OrderByDescending(x => x.Number).Skip(k).Take(n));

    [Theory]
    [MemberData(nameof(TakeValues))]
    public Task where_then_take(int n) => countsShouldMatch(q => q.Where(x => x.Number > 3).Take(n));

    [Theory]
    [MemberData(nameof(SkipTakeValues))]
    public Task where_then_skip_take(int k, int n) =>
        countsShouldMatch(q => q.Where(x => x.Number > 3).OrderBy(x => x.Number).Skip(k).Take(n));

    [Theory]
    [MemberData(nameof(TakeValues))]
    public Task select_scalar_then_take(int n) => countsShouldMatch(q => q.Select(x => x.Number).Take(n));

    [Theory]
    [MemberData(nameof(SkipTakeValues))]
    public Task select_scalar_then_skip_take(int k, int n) =>
        countsShouldMatch(q => q.OrderBy(x => x.Number).Select(x => x.Number).Skip(k).Take(n));

    [Theory]
    [MemberData(nameof(TakeValues))]
    public Task distinct_then_take(int n) => countsShouldMatch(q => q.Select(x => x.Color).Distinct().Take(n));

    [Theory]
    [MemberData(nameof(SkipTakeValues))]
    public Task distinct_then_skip_take(int k, int n) =>
        countsShouldMatch(q => q.Select(x => x.Color).Distinct().Skip(k).Take(n));

    [Theory]
    [MemberData(nameof(TakeValues))]
    public Task select_many_then_take(int n) => countsShouldMatch(q => q.SelectMany(x => x.Children).Take(n));

    [Theory]
    [MemberData(nameof(SkipValues))]
    public Task select_many_then_skip(int k) => countsShouldMatch(q => q.SelectMany(x => x.Children).Skip(k));

    [Theory]
    [MemberData(nameof(SkipTakeValues))]
    public Task select_many_then_skip_take(int k, int n) =>
        countsShouldMatch(q => q.SelectMany(x => x.Children).Skip(k).Take(n));

    [Theory]
    [MemberData(nameof(TakeValues))]
    public Task select_many_where_then_take(int n) =>
        countsShouldMatch(q => q.SelectMany(x => x.Children).Where(c => c.Value > 1).Take(n));

    [Theory]
    [MemberData(nameof(TakeValues))]
    public Task select_many_scalar_then_take(int n) => countsShouldMatch(q => q.SelectMany(x => x.Tags).Take(n));

    [Theory]
    [MemberData(nameof(TakeValues))]
    public Task select_many_distinct_then_take(int n) =>
        countsShouldMatch(q => q.SelectMany(x => x.Tags).Distinct().Take(n));

    [Theory]
    [MemberData(nameof(TakeValues))]
    public Task group_by_then_take(int n) =>
        countsShouldMatch(q => q.GroupBy(x => x.Color).Select(g => new { g.Key, Count = g.Count() }).Take(n));

    [Theory]
    [MemberData(nameof(SkipValues))]
    public async Task any_after_skip(int k)
    {
        var expected = Docs.AsQueryable().Skip(k).Any();

        await using var session = theStore.QuerySession();
        (await session.Query<PagedCountDoc>().Skip(k).AnyAsync()).ShouldBe(expected);
        (await session.Query<PagedCountDoc>().OrderBy(x => x.Number).Skip(k).AnyAsync()).ShouldBe(expected);
        (await session.Query<PagedCountDoc>().SelectMany(x => x.Children).Skip(k).AnyAsync())
            .ShouldBe(Docs.SelectMany(x => x.Children).Skip(k).Any());
    }

    [Theory]
    [MemberData(nameof(SkipTakeValues))]
    public async Task any_after_skip_take(int k, int n)
    {
        var expected = Docs.AsQueryable().Skip(k).Take(n).Any();

        await using var session = theStore.QuerySession();
        (await session.Query<PagedCountDoc>().Skip(k).Take(n).AnyAsync()).ShouldBe(expected);
    }

    // Take()/Skip() before a SelectMany() pages the parent documents, so the count is the number of children on
    // that page of documents. That used to sum every document's children and page the single SUM row instead.
    [Theory]
    [MemberData(nameof(SkipTakeValues))]
    public Task skip_take_then_select_many(int k, int n) =>
        countsShouldMatch(q => q.OrderBy(x => x.Number).Skip(k).Take(n).SelectMany(x => x.Children));

    [Theory]
    [MemberData(nameof(SkipValues))]
    public Task skip_then_select_many(int k) =>
        countsShouldMatch(q => q.OrderBy(x => x.Number).Skip(k).SelectMany(x => x.Children));

    // The SQL a paged count actually sends: the page is a CTE, and the count runs over it.
    [Fact]
    public async Task paged_count_sql_counts_a_paged_cte()
    {
        (await executedCountSql(q => q.OrderBy(x => x.Number).Skip(4).Take(3)))
            .ShouldBe("WITH mt_temp_id_list1CTE as (select d.data from public.mt_doc_pagedcountdoc as d " +
                      "order by CAST(d.data ->> 'Number' as integer) OFFSET $1 LIMIT $2) " +
                      "select count(*) as number from mt_temp_id_list1CTE as d;");
    }

    private async Task<string> executedCountSql<T>(Func<IQueryable<PagedCountDoc>, IQueryable<T>> shape)
    {
        await using var session = theStore.QuerySession();
        var recorder = new CommandRecorder();
        session.Logger = recorder;

        await shape(session.Query<PagedCountDoc>()).CountAsync();

        return recorder.Commands.Single();
    }

    // The fix only wraps a counted statement in a CTE when it is paged. Every unpaged count keeps the exact SQL
    // it sent before #5920, captured by executing each query on master at 1d13d0f3a.
    public static IEnumerable<object[]> UnpagedShapes() =>
    [
        ["plain", "select count(*) as number from public.mt_doc_pagedcountdoc as d;"],
        ["where", "select count(*) as number from public.mt_doc_pagedcountdoc as d where CAST(d.data ->> 'Number' as integer) > $1;"],
        ["order_by", "select count(*) as number from public.mt_doc_pagedcountdoc as d;"],
        ["where_order_by", "select count(*) as number from public.mt_doc_pagedcountdoc as d where CAST(d.data ->> 'Number' as integer) > $1;"],
        ["select", "select count(*) as number from public.mt_doc_pagedcountdoc as d;"],
        ["distinct", "WITH mt_temp_id_list1CTE as (select DISTINCT(d.data ->> 'Color') from public.mt_doc_pagedcountdoc as d) select count(*) as number from mt_temp_id_list1CTE as d;"],
        ["select_many", "select SUM(jsonb_array_length(d.data -> 'Children')) from public.mt_doc_pagedcountdoc as d;"],
        ["select_many_where", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements(CAST(d.data ->> 'Children' as jsonb)) as data from public.mt_doc_pagedcountdoc as d) select count(*) as number from mt_temp_id_list1CTE as d where CAST(d.data ->> 'Value' as integer) > $1;"],
        ["select_many_scalar", "select SUM(jsonb_array_length(CAST(d.data ->> 'Tags' as jsonb))) from public.mt_doc_pagedcountdoc as d;"],
        ["select_many_distinct", "WITH mt_temp_id_list1CTE as (select jsonb_array_elements_text(CAST(d.data ->> 'Tags' as jsonb)) as data from public.mt_doc_pagedcountdoc as d)  , mt_temp_id_list2CTE as (select DISTINCT(CAST(data as integer)) as data from mt_temp_id_list1CTE as d) select count(*) as number from mt_temp_id_list2CTE as d;"],
        ["group_by", "WITH mt_temp_id_list1CTE as (select  jsonb_build_object('Key', d.data ->> 'Color', 'Count', count(*))  as data from public.mt_doc_pagedcountdoc as d GROUP BY d.data ->> 'Color') select count(*) as number from mt_temp_id_list1CTE as d;"]
    ];

    [Theory]
    [MemberData(nameof(UnpagedShapes))]
    public async Task unpaged_count_sql_is_unchanged(string shape, string expected)
    {
        var actual = shape switch
        {
            "plain" => await executedCountSql(q => q),
            "where" => await executedCountSql(q => q.Where(x => x.Number > 3)),
            "order_by" => await executedCountSql(q => q.OrderBy(x => x.Number)),
            "where_order_by" => await executedCountSql(q => q.Where(x => x.Number > 3).OrderBy(x => x.Number)),
            "select" => await executedCountSql(q => q.Select(x => x.Number)),
            "distinct" => await executedCountSql(q => q.Select(x => x.Color).Distinct()),
            "select_many" => await executedCountSql(q => q.SelectMany(x => x.Children)),
            "select_many_where" => await executedCountSql(q => q.SelectMany(x => x.Children).Where(c => c.Value > 1)),
            "select_many_scalar" => await executedCountSql(q => q.SelectMany(x => x.Tags)),
            "select_many_distinct" => await executedCountSql(q => q.SelectMany(x => x.Tags).Distinct()),
            "group_by" => await executedCountSql(q => q.GroupBy(x => x.Color).Select(g => new { g.Key, Count = g.Count() })),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };

        actual.ShouldBe(expected);
    }

    // Records the text of every command the session actually sends, so the SQL assertions above are about what
    // executed rather than about a preview of it.
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
}
