using Marten.Schema;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class optimistic_concurrency : IntegratedFixture
    {
        [Fact]
        public void can_generate_the_upsert_smoke_test_with_94_style()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Legacy;
                _.Schema.For<Issue>().UseOptimisticConcurrency(true);
            });

            theStore.Schema.EnsureStorageExists(typeof(Issue));
        }

        [Fact]
        public void can_generate_the_upsert_smoke_test_with_95_style()
        {
            StoreOptions(_ => {
                _.UpsertType = PostgresUpsertType.Standard;
                _.Schema.For<Issue>().UseOptimisticConcurrency(true);
            });

            theStore.Schema.EnsureStorageExists(typeof(Issue));
        }
    }
}