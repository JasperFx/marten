using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_621_bulk_insert_with_optimistic_concurrency: IntegrationContext
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

        public Bug_621_bulk_insert_with_optimistic_concurrency(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
