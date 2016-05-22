using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Services;
using Marten.Testing.Fixtures;
using Marten.Util;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Linq
{
    public class invoking_query_with_statistics : DocumentSessionFixture<NulloIdentityMap>
    {
        private readonly ITestOutputHelper _output;

        public invoking_query_with_statistics(ITestOutputHelper output)
        {
            _output = output;
            theStore.BulkInsert(Target.GenerateRandomData(100).ToArray());
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


            var cmd = theSession
                .Query<Target>()
                .Stats(out stats)
                .Where(x => x.Number > 10).Take(5)
                .ToCommand();

            _output.WriteLine(cmd.CommandText);
        }
        // ENDSAMPLE

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

        // SAMPLE: compiled-query-statistics
        public class TargetPaginationQuery : ICompiledListQuery<Target>
        {
            public QueryStatistics Stats { get; set; }
            public int PageNumber { get; set; }
            public int PageSize { get; set; }

            public TargetPaginationQuery(int pageNumber, int pageSize)
            {
                PageNumber = pageNumber;
                PageSize = pageSize;
            }

            public Expression<Func<IQueryable<Target>, IEnumerable<Target>>> QueryIs()
            {
                return query => query.Stats<Target,TargetPaginationQuery>(x=>x.Stats)
                    .Where(x => x.Number > 10).Skip(PageNumber).Take(PageSize);
            }
        }

        [Fact]
        public void can_get_the_total_from_a_compiled_query()
        {
            var count = theSession.Query<Target>().Count(x => x.Number > 10);
            count.ShouldBeGreaterThan(0);

            var query = new TargetPaginationQuery(2,5);
            var list = theSession
                .Query(query)
                .ToList();

            list.Any().ShouldBeTrue();
            
            query.Stats.TotalResults.ShouldBe(count);
        }
        // ENDSAMPLE
    }
}