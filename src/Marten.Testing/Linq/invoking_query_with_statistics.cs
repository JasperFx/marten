using System.Linq;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Services;
using Marten.Testing.Fixtures;
using Marten.Util;
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

        [Fact]
        public void can_get_the_total_in_results()
        {
            var count = theSession.Query<Target>().Count(x => x.Number > 10);
            count.ShouldBeGreaterThan(0);


            QueryStatistics stats = null;

            var list = theSession.Query<Target>().Stats(out stats).Where(x => x.Number > 10).Take(5)
                .ToList();

            list.Any().ShouldBeTrue();

            stats.TotalResults.ShouldBe(count);
        }

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
    }
}