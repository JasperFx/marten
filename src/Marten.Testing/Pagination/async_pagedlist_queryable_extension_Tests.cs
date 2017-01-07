using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Shouldly;
using Marten.Services;
using Marten.Pagination;

namespace Marten.Testing.Pagination
{
    public class async_pagedlist_queryable_extension_Tests : DocumentSessionFixture<NulloIdentityMap>
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

        public async_pagedlist_queryable_extension_Tests()
        {
            BuildUpTargetData();
        }

        [Fact]
        public async Task can_return_paged_result()
        {
            var pageNumber = 2;
            var pageSize = 10;

            var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);

            pagedList.Count.ShouldBe(pageSize);
        }

        [Fact]
        public async Task invalid_pagenumber_should_throw_exception()
        {
            // invalid page number
            var pageNumber = 0;

            var pageSize = 10;

            var ex = await Exception<ArgumentOutOfRangeException>.ShouldBeThrownByAsync(async () =>
            {
                await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            });

            ex.Message.ShouldContain("pageNumber = 0. PageNumber cannot be below 1.");
        }

        [Fact]
        public async Task invalid_pagesize_should_throw_exception()
        {
            var pageNumber = 1;

            // invalid page size
            var pageSize = 0;

            var ex = await Exception<ArgumentOutOfRangeException>.ShouldBeThrownByAsync(async () =>
            {
                await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            });

            ex.Message.ShouldContain($"pageSize = 0. PageSize cannot be below 1.");
        }

        [Fact]
        public async Task pagesize_outside_page_range_should_throw_exception()
        {
            // page number ouside the page range, page range is between 1 and 10 for the sample 
            var pageNumber = 11;

            var pageSize = 10;

            var ex = await Exception<ArgumentOutOfRangeException>.ShouldBeThrownByAsync(async () =>
            {
                await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            });

            ex.Message.ShouldContain($"pageNumber = 11. PageNumber is the outside the valid range.");
        }

        [Fact]
        public async Task check_computed_pagecount()
        {
            // page number ouside the page range, page range is between 1 and 10 for the sample 
            var pageNumber = 1;

            var pageSize = 10;

            var expectedPageCount = await theSession.Query<Target>().CountAsync().ConfigureAwait(false) / pageSize;

            var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            pagedList.PageCount.ShouldBe(expectedPageCount);
        }

        [Fact]
        public async Task check_total_items_count()
        {
            var pageNumber = 1;

            var pageSize = 10;

            var expectedTotalItemsCount = await theSession.Query<Target>().CountAsync().ConfigureAwait(false);

            var pagedList = theSession.Query<Target>().ToPagedList(pageNumber, pageSize);
            pagedList.TotalItemCount.ShouldBe(expectedTotalItemsCount);
        }

        [Fact]
        public async Task check_has_previous_page()
        {
            var pageNumber = 2;

            var pageSize = 10;

            var expectedHasPreviousPage = true;

            var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            pagedList.HasPreviousPage.ShouldBe(expectedHasPreviousPage);
        }

        [Fact]
        public async Task check_has_no_previous_page()
        {
            var pageNumber = 1;

            var pageSize = 10;

            var expectedHasPreviousPage = false;

            var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            pagedList.HasPreviousPage.ShouldBe(expectedHasPreviousPage);
        }

        [Fact]
        public async Task check_has_next_page()
        {
            var pageNumber = 1;

            var pageSize = 10;

            var expectedHasNextPage = true;

            var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            pagedList.HasNextPage.ShouldBe(expectedHasNextPage);
        }

        [Fact]
        public async Task check_has_no_next_page()
        {
            var pageNumber = 10;

            var pageSize = 10;

            var expectedHasNextPage = false;

            var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            pagedList.HasNextPage.ShouldBe(expectedHasNextPage);
        }

        [Fact]
        public async Task check_is_first_page()
        {
            var pageNumber = 1;

            var pageSize = 10;

            var expectedIsFirstPage = true;

            var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            pagedList.IsFirstPage.ShouldBe(expectedIsFirstPage);
        }

        [Fact]
        public async Task check_is_not_first_page()
        {
            var pageNumber = 2;

            var pageSize = 10;

            var expectedIsFirstPage = false;

            var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            pagedList.IsFirstPage.ShouldBe(expectedIsFirstPage);
        }

        [Fact]
        public async Task check_is_last_page()
        {
            var pageNumber = 10;

            var pageSize = 10;

            var expectedIsLastPage = true;

            var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            pagedList.IsLastPage.ShouldBe(expectedIsLastPage);
        }

        [Fact]
        public async Task check_is_not_last_page()
        {
            var pageNumber = 1;

            var pageSize = 10;

            var expectedIsLastPage = false;

            var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            pagedList.IsLastPage.ShouldBe(expectedIsLastPage);
        }

        [Fact]
        public async Task check_first_item_on_page()
        {
            var pageNumber = 2;

            var pageSize = 10;

            var expectedFirstItemOnPage = 11;

            var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            pagedList.FirstItemOnPage.ShouldBe(expectedFirstItemOnPage);
        }

        [Fact]
        public async Task check_last_item_on_page()
        {
            var pageNumber = 2;

            var pageSize = 10;

            var expectedLastItemOnPage = 20;

            var pagedList = await theSession.Query<Target>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            pagedList.LastItemOnPage.ShouldBe(expectedLastItemOnPage);
        }

        [Fact]
        public async Task zero_records_document_should_return_pagedlist_gracefully()
        {
            BuildUpDocumentWithZeroRecords();

            var pageNumber = 1;

            var pageSize = 10;

            var pagedList = await theSession.Query<PaginationTestDocument>().ToPagedListAsync(pageNumber, pageSize).ConfigureAwait(false);
            pagedList.TotalItemCount.ShouldBe(0);
            pagedList.PageCount.ShouldBe(0);
            pagedList.IsFirstPage.ShouldBe(false);
            pagedList.IsLastPage.ShouldBe(false);
            pagedList.HasPreviousPage.ShouldBe(false);
            pagedList.HasNextPage.ShouldBe(false);
            pagedList.FirstItemOnPage.ShouldBe(0);
            pagedList.LastItemOnPage.ShouldBe(0);
        }
    }
}
