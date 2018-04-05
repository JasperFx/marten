using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using StructureMap.Building;
using Xunit;

namespace Marten.Testing.MultiTenancy
{
    public class with_optimistic_concurrency : IntegratedFixture
    {
        private Target target = Target.Random();

        public with_optimistic_concurrency()
        {
            StoreOptions(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Policies.AllDocumentsAreMultiTenanted();
                _.Schema.For<Target>().UseOptimisticConcurrency(enabled: true);
            });
        }

        [Fact]
        public async Task composite_key_correctly_used_for_upsert_concurrency_check()
        {
            using (var session = theStore.OpenSession("Red"))
            {
                session.Store(target);
                await session.SaveChangesAsync();
            }

            using (var session = theStore.OpenSession("Blue"))
            {
                session.Store(target);
                await session.SaveChangesAsync();
            }
        }       
    }
}