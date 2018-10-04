using System;
using System.Linq;
using Xunit;
using Shouldly;
using Marten.Services;
using Marten.Pagination;
using System.Threading.Tasks;

namespace Marten.Testing.Pagination
{
    public class PaginationTestDocument
    {
        public string Id { get; set; }
    }

    public class pagedlist_queryable_extension_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        private void BuildUpTargetData()
        {
            var targets = Target.GenerateRandomData(100).ToArray();

            theSession.Store(targets);
            theSession.SaveChanges();
        }

        private void BuildUpDocumentWithZeroRecords()
        {
            var doc = new PaginationTestDocument();
            doc.Id = "test";

            theSession.Store(doc);
            theSession.SaveChanges();

            theSession.Delete<PaginationTestDocument>(doc);
            theSession.SaveChanges();
        }

        public pagedlist_queryable_extension_Tests()
        {
            BuildUpTargetData();
        }

        [Fact]
        public void can_return_paged_result()
        {
            // SAMPLE: to_paged_list
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
            // ENDSAMPLE

            pagedList.Count.ShouldBe(pageSize);
            
        }

        [Fact]
        public async Task can_return_paged_result_async()
        {
            // SAMPLE: to_paged_list_async
            var pageNumber = 2;
            var pageSize = 10;

            var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize)
                .ConfigureAwait(false);
            // ENDSAMPLE

            pagedList.Count.ShouldBe(pageSize);
        }

        [Fact]
        public void invalid_pagenumber_should_throw_exception()
        {
            // invalid page number
            var pageNumber = 0;

            var pageSize = 10;

            var ex =
                Exception<ArgumentOutOfRangeException>.ShouldBeThrownBy(
                    () => theSession.Query<Target>().ToPagedList(pageNumber, pageSize));
            ex.Message.ShouldContain("pageNumber = 0. PageNumber cannot be below 1.");
        }

        [Fact]
        public void invalid_pagesize_should_throw_exception()
        {
            var pageNumber = 1;

            // invalid page size
            var pageSize = 0;

            var ex =
                Exception<ArgumentOutOfRangeException>.ShouldBeThrownBy(
                    () => theSession.Query<Target>().ToPagedList(pageNumber, pageSize));
            ex.Message.ShouldContain($"pageSize = 0. PageSize cannot be below 1.");
        }

        [Fact]
        public void check_computed_pagecount()
        {
            // page number ouside the page range, page range is between 1 and 10 for the sample 
            var pageNumber = 1;

            var pageSize = 10;

            var expectedPageCount = theSession.Query<Target>().Count()/pageSize;

            var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);
            pagedList.PageCount.ShouldBe(expectedPageCount);
        }

        [Fact]
        public void check_total_items_count()
        {
            var pageNumber = 1;

            var pageSize = 10;

            var expectedTotalItemsCount = theSession.Query<Target>().Count();

            var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);
            pagedList.TotalItemCount.ShouldBe(expectedTotalItemsCount);
        }

        [Fact]
        public void check_has_previous_page()
        {
            var pageNumber = 2;

            var pageSize = 10;

            var expectedHasPreviousPage = true;

            var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);
            pagedList.HasPreviousPage.ShouldBe(expectedHasPreviousPage);
        }

        [Fact]
        public void check_has_no_previous_page()
        {
            var pageNumber = 1;

            var pageSize = 10;

            var expectedHasPreviousPage = false;

            var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);
            pagedList.HasPreviousPage.ShouldBe(expectedHasPreviousPage);
        }

        [Fact]
        public void check_has_next_page()
        { 
            var pageNumber = 1;

            var pageSize = 10;

            var expectedHasNextPage = true;

            var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);
            pagedList.HasNextPage.ShouldBe(expectedHasNextPage);
        }

        [Fact]
        public void check_has_no_next_page()
        {
            var pageNumber = 10;

            var pageSize = 10;

            var expectedHasNextPage = false;

            var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);
            pagedList.HasNextPage.ShouldBe(expectedHasNextPage);
        }

        [Fact]
        public void check_is_first_page()
        {
            var pageNumber = 1;

            var pageSize = 10;

            var expectedIsFirstPage = true;

            var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);
            pagedList.IsFirstPage.ShouldBe(expectedIsFirstPage);
        }

        [Fact]
        public void check_is_not_first_page()
        {
            var pageNumber = 2;

            var pageSize = 10;

            var expectedIsFirstPage = false;

            var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);
            pagedList.IsFirstPage.ShouldBe(expectedIsFirstPage);
        }

        [Fact]
        public void check_is_last_page()
        {
            var pageNumber = 10;

            var pageSize = 10;

            var expectedIsLastPage = true;

            var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);
            pagedList.IsLastPage.ShouldBe(expectedIsLastPage);
        }

        [Fact]
        public void check_is_not_last_page()
        {
            var pageNumber = 1;

            var pageSize = 10;

            var expectedIsLastPage = false;

            var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);
            pagedList.IsLastPage.ShouldBe(expectedIsLastPage);
        }

        [Fact]
        public void check_first_item_on_page()
        {
            var pageNumber = 2;

            var pageSize = 10;

            var expectedFirstItemOnPage = 11;

            var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);
            pagedList.FirstItemOnPage.ShouldBe(expectedFirstItemOnPage);
        }

        [Fact]
        public void check_last_item_on_page()
        {
            var pageNumber = 2;

            var pageSize = 10;

            var expectedLastItemOnPage = 20;

            var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);
            pagedList.LastItemOnPage.ShouldBe(expectedLastItemOnPage);
        }

        [Fact]
        public void zero_records_document_should_return_pagedlist_gracefully()
        {
            BuildUpDocumentWithZeroRecords();

            var pageNumber = 1;

            var pageSize = 10;

            var pagedList = theSession.Query<PaginationTestDocument>().ToPagedList(pageNumber, pageSize);
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

        [Fact]
        public void check_query_with_where_clause_followed_by_to_pagedlist()
        {
            var pageNumber = 2;
            var pageSize = 10;

            var pagedList = theSession.Query<Target>().Where(x=>x.Flag).ToPagedList(pageNumber, pageSize);
        }
    }
}
