using System.Linq;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.MultiTenancy
{
    public class using_mix_of_tenanted_and_not_tenanted_documents : IntegratedFixture
    {
        public using_mix_of_tenanted_and_not_tenanted_documents()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().MultiTenanted();
            });
        }

        [Fact]
        public void can_query_on_non_tenanted_documents()
        {
            // SAMPLE: tenancy-mixed-tenancy-non-tenancy-sample
            var greens = Target.GenerateRandomData(10).ToArray();
            var reds = Target.GenerateRandomData(11).ToArray();

            theStore.BulkInsert("Green", greens);
            theStore.BulkInsert("Red", reds);

            var user1 = new User {UserName = "Frank"};
            var user2 = new User {UserName = "Bill"};

            theStore.BulkInsert(new User[]{user1, user2});

            using (var green = theStore.QuerySession("Green"))
            {
                green.Query<User>().Count().ShouldBe(2);
                green.Query<Target>().Count().ShouldBe(10);
            }

            using (var red = theStore.QuerySession("Red"))
            {
                red.Query<User>().Count().ShouldBe(2);
                red.Query<Target>().Count().ShouldBe(11);
            }
            // ENDSAMPLE
        }
    }
}