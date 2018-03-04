using System.Linq;
using Xunit;

namespace Marten.Testing.MultiTenancy
{
    public class searching_across_tenants : IntegratedFixture
    {
        private readonly Target[] reds = Target.GenerateRandomData(50).ToArray();
        private readonly Target[] greens = Target.GenerateRandomData(75).ToArray();
        private readonly Target[] blues = Target.GenerateRandomData(25).ToArray();

        public searching_across_tenants()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().MultiTenanted();
            });

            theStore.BulkInsert("Red", reds);
            theStore.BulkInsert("Green", greens);
            theStore.BulkInsert("Blue", blues);
        }

        [Fact]
        public void query_within_all_tenants()
        {
            var expected = reds.Concat(greens).Concat(blues)
                               .Where(x => x.Flag).Select(x => x.Id).OrderBy(x => x).ToArray();

            using (var query = theStore.QuerySession())
            {
                // SAMPLE: any_tenant
                // query data across all tenants
                var actual = query.Query<Target>().Where(x => x.AnyTenant() && x.Flag)
                                  .OrderBy(x => x.Id).Select(x => x.Id).ToArray();
                // ENDSAMPLE

                actual.ShouldHaveTheSameElementsAs(expected);
            }
        }

        [Fact]
        public void query_within_selected_tenants()
        {
            var expected = reds.Concat(greens)
                               .Where(x => x.Flag).Select(x => x.Id).OrderBy(x => x).ToArray();

            using (var query = theStore.QuerySession())
            {
                // SAMPLE: tenant_is_one_of
                // query data for a selected list of tenants
                var actual = query.Query<Target>().Where(x => x.TenantIsOneOf("Green", "Red") && x.Flag)
                                  .OrderBy(x => x.Id).Select(x => x.Id).ToArray();
                // ENDSAMPLE

                actual.ShouldHaveTheSameElementsAs(expected);
            }
        }
    }
}