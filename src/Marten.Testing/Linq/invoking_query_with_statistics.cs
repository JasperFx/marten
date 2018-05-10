using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class invoking_query_with_statistics : DocumentSessionFixture<NulloIdentityMap>
    {
        public invoking_query_with_statistics()
        {
            theStore.BulkInsert(Target.GenerateRandomData(100).ToArray());
        }

        // SAMPLE: compiled-query-statistics
        public class TargetPaginationQuery : ICompiledListQuery<Target>
        {
            public TargetPaginationQuery(int pageNumber, int pageSize)
            {
                PageNumber = pageNumber;
                PageSize = pageSize;
            }

            public QueryStatistics Stats { get; set; }
            public int PageNumber { get; set; }
            public int PageSize { get; set; }

            public Expression<Func<IQueryable<Target>, IEnumerable<Target>>> QueryIs()
            {
                return query => query.Stats<Target, TargetPaginationQuery>(x => x.Stats)
                    .Where(x => x.Number > 10).Skip(PageNumber).Take(PageSize);
            }
        }
        // ENDSAMPLE

        [Fact]
        public void can_get_the_total_from_a_compiled_query()
        {
            var count = theSession.Query<Target>().Count(x => x.Number > 10);
            count.ShouldBeGreaterThan(0);

            var query = new TargetPaginationQuery(2, 5);
            var list = theSession
                .Query(query)
                .ToList();

            list.Any().ShouldBeTrue();

            query.Stats.TotalResults.ShouldBe(count);
        }

        [Fact]
        public async Task can_get_the_total_from_a_compiled_query_running_in_a_batch()
        {
            var count = await theSession.Query<Target>().Where(x => x.Number > 10).CountAsync().ConfigureAwait(false);
            count.ShouldBeGreaterThan(0);

            var query = new TargetPaginationQuery(2, 5);

            var batch = theSession.CreateBatchQuery();

            var targets = batch.Query(query);

            await batch.Execute().ConfigureAwait(false);

            (await targets.ConfigureAwait(false))
                .Any().ShouldBeTrue();

            query.Stats.TotalResults.ShouldBe(count);
        }

        [Fact]
        public void can_get_the_total_from_a_compiled_query_running_in_a_batch_sync()
        {
            var count = theSession.Query<Target>().Count(x => x.Number > 10);
            count.ShouldBeGreaterThan(0);

            var query = new TargetPaginationQuery(2, 5);

            var batch = theSession.CreateBatchQuery();

            var targets = batch.Query(query);

            batch.ExecuteSynchronously();

            targets.Result
                .Any().ShouldBeTrue();

            query.Stats.TotalResults.ShouldBe(count);
        }

        [Fact]
        public async Task can_get_the_total_in_batch_query()
        {
            var count = await theSession.Query<Target>().Where(x => x.Number > 10).CountAsync().ConfigureAwait(false);
            count.ShouldBeGreaterThan(0);


            QueryStatistics stats = null;

            var batch = theSession.CreateBatchQuery();

            var list = batch.Query<Target>().Stats(out stats).Where(x => x.Number > 10).Take(5)
                .ToList();

            await batch.Execute().ConfigureAwait(false);


            (await list.ConfigureAwait(false)).Any().ShouldBeTrue();

            stats.TotalResults.ShouldBe(count);
        }

        [Fact]
        public void can_get_the_total_in_batch_query_sync()
        {
            var count = theSession.Query<Target>().Count(x => x.Number > 10);
            count.ShouldBeGreaterThan(0);


            QueryStatistics stats = null;

            var batch = theSession.CreateBatchQuery();

            var list = batch.Query<Target>().Stats(out stats).Where(x => x.Number > 10).Take(5)
                .ToList();

            batch.ExecuteSynchronously();

            list.Result.Any().ShouldBeTrue();

            stats.TotalResults.ShouldBe(count);
        }

        // SAMPLE: using-query-statistics
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

        // ENDSAMPLE

        [Fact]
        public async Task can_get_the_total_in_results_async()
        {
            var count = await theSession.Query<Target>().Where(x => x.Number > 10).CountAsync().ConfigureAwait(false);
            count.ShouldBeGreaterThan(0);


            QueryStatistics stats = null;

            var list = await theSession.Query<Target>().Stats(out stats).Where(x => x.Number > 10).Take(5)
                .ToListAsync().ConfigureAwait(false);

            list.Any().ShouldBeTrue();

            stats.TotalResults.ShouldBe(count);
        }

        // ENDSAMPLE
    }
}