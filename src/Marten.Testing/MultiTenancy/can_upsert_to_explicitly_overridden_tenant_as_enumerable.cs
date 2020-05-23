using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Testing.TrackingSession;
using Shouldly;
using Xunit;

namespace Marten.Testing.MultiTenancy
{
    public class can_upsert_to_explicitly_overridden_tenant_as_enumerable :IntegrationContext
    {
        private readonly Target[] reds = Target.GenerateRandomData(50).ToArray();
        private readonly Target[] greens = Target.GenerateRandomData(75).ToArray();
        private readonly Target[] blues = Target.GenerateRandomData(25).ToArray();

        [Fact]
        public void write_to_tenant()
        {
            StoreOptions(_ =>
            {
                _.Policies.AllDocumentsAreMultiTenanted();
            });

            using (var session = theStore.OpenSession())
            {
                session.Store("Red", reds.AsEnumerable());
                session.Store("Green", greens.AsEnumerable());
                session.Store("Blue", blues.AsEnumerable());

                session.SaveChanges();
            }

            using (var red = theStore.QuerySession("Red"))
            {
                red.Query<Target>().Count().ShouldBe(50);
            }

            using (var green = theStore.QuerySession("Green"))
            {
                green.Query<Target>().Count().ShouldBe(75);
            }

            using (var blue = theStore.QuerySession("Blue"))
            {
                blue.Query<Target>().Count().ShouldBe(25);
            }
        }

        public can_upsert_to_explicitly_overridden_tenant_as_enumerable(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
