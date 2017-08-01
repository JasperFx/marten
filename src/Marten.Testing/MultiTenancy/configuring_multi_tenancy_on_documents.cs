using System;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.MultiTenancy
{
    public class configuring_multi_tenancy_on_documents : IntegratedFixture
    {
        [Fact]
        public void document_type_decorated_with_attribute()
        {
            var mapping = DocumentMapping.For<TenantedDoc>();
            mapping.TenancyStyle.ShouldBe(TenancyStyle.Conjoined);
        }

        [Fact]
        public void use_fluent_interface()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>().MultiTenanted();
            });

            theStore.Storage.MappingFor(typeof(User)).TenancyStyle.ShouldBe(TenancyStyle.Conjoined);

            // the "control" to see that the default rules apply otherwise
            theStore.Storage.MappingFor(typeof(Target)).TenancyStyle.ShouldBe(TenancyStyle.Single);
        }
    }

    [MultiTenanted]
    public class TenantedDoc
    {
        public Guid Id;
    }
}