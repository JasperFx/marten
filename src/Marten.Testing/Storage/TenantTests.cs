using Marten.Internal;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Storage
{
    public class TenantTests
    {
        [Fact]
        public void use_the_baseline_provider_graph_if_options_autocreate_is_none()
        {
            var options = new StoreOptions
            {
                AutoCreateSchemaObjects = AutoCreate.None
            };

            var tenant = new Tenant(options.Storage, options, new ConnectionFactory(ConnectionSource.ConnectionString), "foo");

            tenant.Providers.ShouldBeSameAs(options.Providers);
        }

        [Fact]
        public void use_checking_provider_graph_if_options_autocreate_is_not_none()
        {
            var options = new StoreOptions
            {
                AutoCreateSchemaObjects = AutoCreate.All
            };

            var tenant = new Tenant(options.Storage, options, new ConnectionFactory(ConnectionSource.ConnectionString), "foo");

            tenant.Providers.ShouldBeOfType<StorageCheckingProviderGraph>()
                .Tenant.ShouldBeSameAs(tenant);
        }
    }
}
