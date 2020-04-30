using System.Linq;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.MultiTenancy
{
    public class using_mix_of_tenanted_and_not_tenanted_documents : IntegrationContext
    {


        [Fact]
        public void can_query_on_multi_tenanted_and_non_tenanted_documents()
        {
            // SAMPLE: tenancy-mixed-tenancy-non-tenancy-sample
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().MultiTenanted(); // tenanted
                _.Schema.For<User>(); // non-tenanted
                _.Schema.For<Issue>().MultiTenanted(); // tenanted
            });

            // Add documents to tenant Green
            var greens = Target.GenerateRandomData(10).ToArray();
            theStore.BulkInsert("Green", greens);

            // Add documents to tenant Red
            var reds = Target.GenerateRandomData(11).ToArray();
            theStore.BulkInsert("Red", reds);

            // Add non-tenanted documents
            // User is non-tenanted in schema
            var user1 = new User {UserName = "Frank"};
            var user2 = new User {UserName = "Bill"};
            theStore.BulkInsert(new User[]{user1, user2});

            // Add documents to default tenant
            // Note that schema for Issue is multi-tenanted hence documents will get added
            // to default tenant if tenant is not passed in the bulk insert operation
            var issue1 = new Issue { Title = "Test issue1" };
            var issue2 = new Issue { Title = "Test issue2" };
            theStore.BulkInsert(new Issue[] { issue1, issue2 });

            // Create a session with tenant Green
            using (var session = theStore.QuerySession("Green"))
            {
                // Query tenanted document as the tenant passed in session
                session.Query<Target>().Count().ShouldBe(10);

                // Query non-tenanted documents
                session.Query<User>().Count().ShouldBe(2);

                // Query documents in default tenant from a session using tenant Green
                session.Query<Issue>().Count(x => x.TenantIsOneOf(Tenancy.DefaultTenantId)).ShouldBe(2);

                // Query documents from tenant Red from a session using tenant Green
                session.Query<Target>().Count(x => x.TenantIsOneOf("Red")).ShouldBe(11);
            }

            // create a session without passing any tenant, session will use default tenant
            using (var session = theStore.QuerySession())
            {
                // Query non-tenanted documents
                session.Query<User>().Count().ShouldBe(2);

                // Query documents in default tenant
                // Note that session is using default tenant
                session.Query<Issue>().Count().ShouldBe(2);

                // Query documents on tenant Green
                session.Query<Target>().Count(x => x.TenantIsOneOf("Green")).ShouldBe(10);

                // Query documents on tenant Red
                session.Query<Target>().Count(x => x.TenantIsOneOf("Red")).ShouldBe(11);
            }
            // ENDSAMPLE
        }

        public using_mix_of_tenanted_and_not_tenanted_documents(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
