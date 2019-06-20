using System.Linq;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_621_bulk_insert_with_optimistic_concurrency: IntegratedFixture
    {
        [Fact]
        public void can_do_a_bulk_insert()
        {
            var targets = Target.GenerateRandomData(1000).ToArray();

            StoreOptions(_ =>
            {
                _.Schema.For<Target>().UseOptimisticConcurrency(true);
            });

            theStore.BulkInsert(targets);

            using (var query = theStore.QuerySession())
            {
                query.Query<Target>().Count().ShouldBe(1000);
            }
        }
    }
}
