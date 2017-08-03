using System;
using Marten.Schema;
using Marten.Storage;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class document_policies_can_be_overridden_by_attributes : IntegratedFixture
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
                // SAMPLE: tenancy-configure-override
                storeOptions.Policies.ForAllDocuments(x => x.TenancyStyle = TenancyStyle.Single);  
                storeOptions.Schema.For<Target>().MultiTenanted();
                // ENDSAMPLE
            });

            theStore.Storage.MappingFor(typeof(Target))
                .TenancyStyle.ShouldBe(TenancyStyle.Conjoined);
        }


        [MultiTenanted]
        public class TenantedDoc
        {
            public Guid Id;
        }
    }
}