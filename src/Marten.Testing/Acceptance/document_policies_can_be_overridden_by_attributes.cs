using System;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class document_policies_can_be_overridden_by_attributes: IntegrationContext
    {
        [Fact]
        public void attribute_can_override_a_policy()
        {
            StoreOptions(_ =>
            {
                _.Policies.ForAllDocuments(x => x.TenancyStyle = TenancyStyle.Single);
            });

            theStore.Storage.MappingFor(typeof(TenantedDoc))
                .TenancyStyle.ShouldBe(TenancyStyle.Conjoined);
        }

        [Fact]
        public void fluent_interface_overrides_policies()
        {
            StoreOptions(storeOptions =>
            {
                #region sample_tenancy-configure-override
                storeOptions.Policies.ForAllDocuments(x => x.TenancyStyle = TenancyStyle.Single);
                storeOptions.Schema.For<Target>().MultiTenanted();
                #endregion sample_tenancy-configure-override
            });

            theStore.Storage.MappingFor(typeof(Target))
                .TenancyStyle.ShouldBe(TenancyStyle.Conjoined);
        }

        [MultiTenanted]
        public class TenantedDoc
        {
            public Guid Id;
        }

        public document_policies_can_be_overridden_by_attributes(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
