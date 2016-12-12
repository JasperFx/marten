using System;
using System.Linq;
using Xunit;
using Shouldly;
using Marten.Services;

namespace Marten.Testing.Linq
{
    public class invoking_queryable_pagedresult_Tests: DocumentSessionFixture<NulloIdentityMap>
    {
        private void buildUpTargetData()
        {
            var targets = Target.GenerateRandomData(15).ToArray();

            theSession.Store(targets);

            theSession.SaveChanges();
        }

        [Fact]
        public void can_return_paged_result()
        {
            buildUpTargetData();

            var pageIndex = 1;
            var pageSize = 10;

            theSession.Query<Target>().PagedResult(pageIndex, pageSize)
                .Count().ShouldBe<int>(pageSize);

            theSession.Query<Target>().PagedResult(pageIndex+1, pageSize)
                .Count().ShouldBe<int>(5);
        }

        [Fact]
        public void can_return_paged_result_with_orderby()
        {
            buildUpTargetData();

            var pageIndex = 1;
            var pageSize = 10;

            Func<IQueryable<Target>, IQueryable<Target>> order = null;
            order = q => q.OrderBy(m => m.Date);

            var results = theSession.Query<Target>().PagedResult(pageIndex, pageSize, order);

            results[0].Date.ShouldBeLessThan(results[results.Count() - 1].Date);
        }

        [Fact]
        public void can_return_paged_result_async()
        {
            buildUpTargetData();

            var pageIndex = 1;
            var pageSize = 10;

            theSession.Query<Target>().PagedResultAsync(pageIndex, pageSize)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult()
                .Count()
                .ShouldBe<int>(pageSize);

            theSession.Query<Target>().PagedResultAsync(pageIndex+1, pageSize)
               .ConfigureAwait(false)
               .GetAwaiter()
               .GetResult()
               .Count()
               .ShouldBe<int>(5);
        }

        [Fact]
        public void can_return_paged_result_async_with_orderby()
        {
            buildUpTargetData();

            var pageIndex = 1;
            var pageSize = 10;

            Func<IQueryable<Target>, IQueryable<Target>> order = null;

            order = q => q.OrderByDescending(m => m.Date);

            var results = theSession.Query<Target>().PagedResultAsync(pageIndex, pageSize, order)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            results[0].Date.ShouldBeGreaterThan(results[results.Count() - 1].Date);
        }
    }
}
