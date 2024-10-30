using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Pagination;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Acceptance;

public class PaginationTestDocument
{
    public string Id { get; set; }
}

public class ToPagedListData<T> : IEnumerable<object[]>
{
    private static readonly Func<IQueryable<T>, int, int, Task<IPagedList<T>>> ToPagedListAsync
        = (query, pageNumber, pageSize) => query.ToPagedListAsync(pageNumber, pageSize);

    private static readonly Func<IQueryable<T>, int, int, Task<IPagedList<T>>> ToPagedListSync
        = (query, pageNumber, pageSize) => Task.FromResult(query.ToPagedList(pageNumber, pageSize));

    public IEnumerator<object[]> GetEnumerator()
    {
        yield return new object []{ ToPagedListAsync };
        yield return new object[] { ToPagedListSync };
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public class statistics_and_paged_list: IntegrationContext
{
    public statistics_and_paged_list(DefaultStoreFixture fixture) : base(fixture)
    {

    }

    protected override Task fixtureSetup()
    {
        return theStore.BulkInsertAsync(Target.GenerateRandomData(100).ToArray());
    }

    #region sample_compiled-query-statistics
    public class TargetPaginationQuery: ICompiledListQuery<Target>
    {
        public TargetPaginationQuery(int pageNumber, int pageSize)
        {
            PageNumber = pageNumber;
            PageSize = pageSize;
        }

        public int PageNumber { get; set; }
        public int PageSize { get; set; }

        public QueryStatistics Stats { get; } = new QueryStatistics();

        public Expression<Func<IMartenQueryable<Target>, IEnumerable<Target>>> QueryIs()
        {
            return query => query
                .Where(x => x.Number > 10)
                .Skip(PageNumber)
                .Take(PageSize);
        }
    }

    #endregion

    [Fact]
    public async Task can_get_the_total_from_a_compiled_query()
    {
        var count = theSession.Query<Target>().Count(x => x.Number > 10);
        count.ShouldBeGreaterThan(0);

        var query = new TargetPaginationQuery(2, 5);
        var list = (await theSession
            .QueryAsync(query))
            .ToList();

        list.Any().ShouldBeTrue();

        query.Stats.TotalResults.ShouldBe(count);
    }

    [Fact]
    public async Task can_use_json_streaming_with_statistics()
    {

        var count = theSession.Query<Target>().Count(x => x.Number > 10);
        count.ShouldBeGreaterThan(0);

        var query = new TargetPaginationQuery(2, 5);
        var stream = new MemoryStream();
        var resultCount = await theSession
            .StreamJsonMany(query, stream);

        resultCount.ShouldBeGreaterThan(0);

        stream.Position = 0;
        var list = theStore.Options.Serializer().FromJson<Target[]>(stream);
        list.Length.ShouldBe(5);

    }

    [Fact]
    public async Task can_get_the_total_from_a_compiled_query_running_in_a_batch()
    {
        var count = await theSession.Query<Target>().Where(x => x.Number > 10).CountAsync();
        count.ShouldBeGreaterThan(0);

        var query = new TargetPaginationQuery(2, 5);

        var batch = theSession.CreateBatchQuery();

        var targets = batch.Query(query);

        await batch.Execute();

        (await targets)
            .Any().ShouldBeTrue();

        query.Stats.TotalResults.ShouldBe(count);
    }

    [Fact]
    public async Task can_get_the_total_from_a_compiled_query_running_in_a_batch_sync()
    {
        var count = theSession.Query<Target>().Count(x => x.Number > 10);
        count.ShouldBeGreaterThan(0);

        var query = new TargetPaginationQuery(2, 5);

        var batch = theSession.CreateBatchQuery();

        var targets = batch.Query(query);

        await batch.Execute();

        (await targets)
            .Any().ShouldBeTrue();

        query.Stats.TotalResults.ShouldBe(count);
    }

    [Fact]
    public async Task can_get_the_total_in_batch_query()
    {
        var count = await theSession.Query<Target>().Where(x => x.Number > 10).CountAsync();
        count.ShouldBeGreaterThan(0);

        QueryStatistics stats = null;

        var batch = theSession.CreateBatchQuery();

        var list = batch.Query<Target>().Stats(out stats).Where(x => x.Number > 10).Take(5)
            .ToList();

        await batch.Execute();

        (await list).Any().ShouldBeTrue();

        stats.TotalResults.ShouldBe(count);
    }

    [Fact]
    public async Task can_get_the_total_in_batch_query_sync()
    {
        var count = theSession.Query<Target>().Count(x => x.Number > 10);
        count.ShouldBeGreaterThan(0);

        QueryStatistics stats = null;

        var batch = theSession.CreateBatchQuery();

        var list = batch.Query<Target>().Stats(out stats).Where(x => x.Number > 10).Take(5)
            .ToList();

        await batch.Execute();

        (await list).Any().ShouldBeTrue();

        stats.TotalResults.ShouldBe(count);
    }

    #region sample_using-query-statistics
    [Fact]
    public void can_get_the_total_in_results()
    {
        var count = theSession.Query<Target>().Count(x => x.Number > 10);
        count.ShouldBeGreaterThan(0);

        // We're going to use stats as an output
        // parameter to the call below, so we
        // have to declare the "stats" object
        // first
        QueryStatistics stats = null;

        var list = theSession
            .Query<Target>()
            .Stats(out stats)
            .Where(x => x.Number > 10).Take(5)
            .ToList();

        list.Any().ShouldBeTrue();

        // Now, the total results data should
        // be available
        stats.TotalResults.ShouldBe(count);
    }

    #endregion

    [Fact]
    public async Task can_get_the_total_in_results_async()
    {
        var count = await theSession.Query<Target>().Where(x => x.Number > 10).CountAsync();
        count.ShouldBeGreaterThan(0);

        QueryStatistics stats = null;

        var list = await theSession.Query<Target>().Stats(out stats).Where(x => x.Number > 10).Take(5)
            .ToListAsync();

        list.Any().ShouldBeTrue();

        stats.TotalResults.ShouldBe(count);
    }

     private async Task BuildUpDocumentWithZeroRecords()
    {
        var doc = new PaginationTestDocument();
        doc.Id = "test";

        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        theSession.Delete<PaginationTestDocument>(doc);
        await theSession.SaveChangesAsync();
    }

    [Fact]
    public void can_return_paged_result()
    {
        #region sample_to_paged_list
        var pageNumber = 2;
        var pageSize = 10;

        var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);

        // paged list also provides a list of helper properties to deal with pagination aspects
        var totalItems = pagedList.TotalItemCount; // get total number records
        var pageCount = pagedList.PageCount; // get number of pages
        var isFirstPage = pagedList.IsFirstPage; // check if current page is first page
        var isLastPages = pagedList.IsLastPage; // check if current page is last page
        var hasNextPage = pagedList.HasNextPage; // check if there is next page
        var hasPrevPage = pagedList.HasPreviousPage; // check if there is previous page
        var firstItemOnPage = pagedList.FirstItemOnPage; // one-based index of first item in current page
        var lastItemOnPage = pagedList.LastItemOnPage; // one-based index of last item in current page
        #endregion

        pagedList.Count.ShouldBe(pageSize);

    }

    [Fact]
    public async Task can_return_paged_result_async()
    {
        #region sample_to_paged_list_async
        var pageNumber = 2;
        var pageSize = 10;

        var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize);
        #endregion

        pagedList.Count.ShouldBe(pageSize);
    }
    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task invalid_pagenumber_should_throw_exception(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        // invalid page number
        var pageNumber = 0;

        var pageSize = 10;

        var ex =
            await Should.ThrowAsync<ArgumentOutOfRangeException>(
                async () => await toPagedList(theSession.Query<Target>(), pageNumber, pageSize));
        ex.Message.ShouldContain("pageNumber = 0. PageNumber cannot be below 1.");
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task invalid_pagesize_should_throw_exception(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        var pageNumber = 1;

        // invalid page size
        var pageSize = 0;

        var ex =
            await Should.ThrowAsync<ArgumentOutOfRangeException>(
                async () =>  await toPagedList(theSession.Query<Target>(), pageNumber, pageSize));
        ex.Message.ShouldContain($"pageSize = 0. PageSize cannot be below 1.");
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task check_computed_pagecount(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        // page number ouside the page range, page range is between 1 and 10 for the sample
        var pageNumber = 1;

        var pageSize = 10;

        var expectedPageCount = theSession.Query<Target>().Count()/pageSize;

        var pagedList = await toPagedList(theSession.Query<Target>(), pageNumber, pageSize);
        pagedList.PageCount.ShouldBe(expectedPageCount);
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task check_total_items_count(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        var pageNumber = 1;

        var pageSize = 10;

        var expectedTotalItemsCount = theSession.Query<Target>().Count();

        var pagedList = await toPagedList(theSession.Query<Target>(), pageNumber, pageSize);
        pagedList.TotalItemCount.ShouldBe(expectedTotalItemsCount);
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task check_has_previous_page(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        var pageNumber = 2;

        var pageSize = 10;

        var expectedHasPreviousPage = true;

        var pagedList = await toPagedList(theSession.Query<Target>(), pageNumber, pageSize);
        pagedList.HasPreviousPage.ShouldBe(expectedHasPreviousPage);
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task check_has_no_previous_page(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        var pageNumber = 1;

        var pageSize = 10;

        var expectedHasPreviousPage = false;

        var pagedList = await toPagedList(theSession.Query<Target>(), pageNumber, pageSize);
        pagedList.HasPreviousPage.ShouldBe(expectedHasPreviousPage);
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task check_has_next_page(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        var pageNumber = 1;

        var pageSize = 10;

        var expectedHasNextPage = true;

        var pagedList = await toPagedList(theSession.Query<Target>(), pageNumber, pageSize);
        pagedList.HasNextPage.ShouldBe(expectedHasNextPage);
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task check_has_no_next_page(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        var pageNumber = 10;

        var pageSize = 10;

        var expectedHasNextPage = false;

        var pagedList = await toPagedList(theSession.Query<Target>(), pageNumber, pageSize);
        pagedList.HasNextPage.ShouldBe(expectedHasNextPage);
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task check_is_first_page(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        var pageNumber = 1;

        var pageSize = 10;

        var expectedIsFirstPage = true;

        var pagedList = await toPagedList(theSession.Query<Target>(), pageNumber, pageSize);
        pagedList.IsFirstPage.ShouldBe(expectedIsFirstPage);
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task check_is_not_first_page(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        var pageNumber = 2;

        var pageSize = 10;

        var expectedIsFirstPage = false;

        var pagedList = await toPagedList(theSession.Query<Target>(), pageNumber, pageSize);
        pagedList.IsFirstPage.ShouldBe(expectedIsFirstPage);
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task check_is_last_page(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        var pageNumber = 10;

        var pageSize = 10;

        var expectedIsLastPage = true;

        var pagedList = await toPagedList(theSession.Query<Target>(), pageNumber, pageSize);
        pagedList.IsLastPage.ShouldBe(expectedIsLastPage);
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task check_is_not_last_page(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        var pageNumber = 1;

        var pageSize = 10;

        var expectedIsLastPage = false;

        var pagedList = await toPagedList(theSession.Query<Target>(), pageNumber, pageSize);
        pagedList.IsLastPage.ShouldBe(expectedIsLastPage);
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task check_first_item_on_page(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        var pageNumber = 2;

        var pageSize = 10;

        var expectedFirstItemOnPage = 11;

        var pagedList = await toPagedList(theSession.Query<Target>(), pageNumber, pageSize);
        pagedList.FirstItemOnPage.ShouldBe(expectedFirstItemOnPage);
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task check_last_item_on_page(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        var pageNumber = 2;

        var pageSize = 10;

        var expectedLastItemOnPage = 20;

        var pagedList = await toPagedList(theSession.Query<Target>(), pageNumber, pageSize);
        pagedList.LastItemOnPage.ShouldBe(expectedLastItemOnPage);
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<PaginationTestDocument>))]
    public async Task zero_records_document_should_return_pagedlist_gracefully(Func<IQueryable<PaginationTestDocument>, int, int, Task<IPagedList<PaginationTestDocument>>> toPagedList)
    {
        // Test failure bomb
        if (DateTime.Today < new DateTime(2023, 9, 5)) return;

        await BuildUpDocumentWithZeroRecords();

        var pageNumber = 1;

        var pageSize = 10;

        var pagedList = await toPagedList(theSession.Query<PaginationTestDocument>(), pageNumber, pageSize);
        pagedList.TotalItemCount.ShouldBe(0);
        pagedList.PageCount.ShouldBe(0);
        pagedList.IsFirstPage.ShouldBe(false);
        pagedList.IsLastPage.ShouldBe(false);
        pagedList.HasPreviousPage.ShouldBe(false);
        pagedList.HasNextPage.ShouldBe(false);
        pagedList.FirstItemOnPage.ShouldBe(0);
        pagedList.LastItemOnPage.ShouldBe(0);
        pagedList.PageNumber.ShouldBe(pageNumber);
        pagedList.PageSize.ShouldBe(pageSize);
    }

    [Theory]
    [ClassData(typeof(ToPagedListData<Target>))]
    public async Task check_query_with_where_clause_followed_by_to_pagedlist(Func<IQueryable<Target>, int, int, Task<IPagedList<Target>>> toPagedList)
    {
        var pageNumber = 2;
        var pageSize = 10;

        var pagedList = await theSession.Query<Target>().Where(x=>x.Flag).ToPagedListAsync(pageNumber, pageSize);
    }

    [Fact]
    public async Task try_to_use_in_compiled_query()
    {
        await Should.ThrowAsync<BadLinqExpressionException>(async () =>
        {
            var data = await theSession.QueryAsync(new TargetPage(1, 10));
        });
    }

    public class TargetPage: ICompiledQuery<Target, IPagedList<Target>>
    {
        public int Page { get; }
        public int PageSize { get; }

        public TargetPage(int page, int pageSize)
        {
            Page = page;
            PageSize = pageSize;
        }

        public Expression<Func<IMartenQueryable<Target>, IPagedList<Target>>> QueryIs()
        {
            return q => q.OrderBy(x => x.Number).ToPagedList(Page, PageSize);
        }
    }
}
