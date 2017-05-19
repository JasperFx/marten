using System.Threading.Tasks;
using Marten.Testing.Examples;
using Shouldly;
using Xunit;

namespace Marten.Testing.MultiTenancy
{
    public class fetch_tenant_id_as_part_of_metadata : IntegratedFixture
    {
        [Fact]
        public void tenant_id_on_metadata()
        {
            StoreOptions(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString)
                    .MultiTenanted();
            });

            var user1 = new User();
            var user2 = new User();

            theStore.BulkInsert("Green", new User[] {user1});
            theStore.BulkInsert("Purple", new User[] {user2});

            theStore.Tenancy.Default.MetadataFor(user1)
                .TenantId.ShouldBe("Green");
        }

        [Fact]
        public async Task tenant_id_on_metadata_async()
        {
            StoreOptions(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString)
                 .MultiTenanted();
            });

            var user1 = new User();
            var user2 = new User();

            theStore.BulkInsert("Green", new User[] { user1 });
            theStore.BulkInsert("Purple", new User[] { user2 });

            var metadata = await theStore.Tenancy.Default.MetadataForAsync(user1);
            metadata.TenantId.ShouldBe("Green");

        }
    }
}