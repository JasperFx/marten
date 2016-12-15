using System;
using System.Linq;
using Xunit;
using Shouldly;
using Marten.Services;
using Marten.Pagination;

namespace Marten.Testing.Pagination
{
    public class invoking_pagedquery_Tests: DocumentSessionFixture<NulloIdentityMap>
    {
        private void BuildUpTargetData()
        {
            var targets = Target.GenerateRandomData(15).ToArray();

            theSession.Store(targets);

            theSession.SaveChanges();
        }

        [Fact]
        public void can_return_paged_result()
        {
            this.BuildUpTargetData();

            var pageIndex = 1;
            var pageSize = 10;

            var result = theSession.Query<Target>().PagedQuery(pageIndex, pageSize);
                
            result.Count.ShouldBe<int>(pageSize);
            result.MetaData.IsFirstPage.ShouldBeTrue();
            result.MetaData.IsLastPage.ShouldBeFalse();
            result.MetaData.TotalItemCount.ShouldBe<int>(15);
            result.MetaData.HasPreviousPage.ShouldBeFalse();
            result.MetaData.HasNextPage.ShouldBeTrue();

            theSession.Query<Target>().PagedQuery(pageIndex+1, pageSize)
                .Count.ShouldBe(5);
        }

        [Fact]
        public void can_return_paged_result_with_orderby()
        {
            this.BuildUpTargetData();

            var pageIndex = 1;
            var pageSize = 10;

            Func<IQueryable<Target>, IQueryable<Target>> order = null;
            order = q => q.OrderBy(m => m.Date);

            var result = theSession.Query<Target>().PagedQuery(pageIndex, pageSize, order);

            result[0].Date.ShouldBeLessThan(result[result.Count - 1].Date);
        }

        [Fact]
        public void can_return_paged_result_async()
        {
            this.BuildUpTargetData();

            var pageIndex = 1;
            var pageSize = 10;

            theSession.Query<Target>().PagedQueryAsync(pageIndex, pageSize)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult()
                .Count
                .ShouldBe<int>(pageSize);

            theSession.Query<Target>().PagedQueryAsync(pageIndex+1, pageSize)
               .ConfigureAwait(false)
               .GetAwaiter()
               .GetResult()
               .Count
               .ShouldBe<int>(5);
        }

        [Fact]
        public void can_return_paged_result_async_with_orderby()
        {
            this.BuildUpTargetData();

            var pageIndex = 1;
            var pageSize = 10;

            Func<IQueryable<Target>, IQueryable<Target>> order = null;

            order = q => q.OrderByDescending(m => m.Date);

            var result = theSession.Query<Target>().PagedQueryAsync(pageIndex, pageSize, order)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            result[0].Date.ShouldBeGreaterThan(result[result.Count - 1].Date);
        }
    }
}
