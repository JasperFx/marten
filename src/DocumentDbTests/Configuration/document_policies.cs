using System;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.Configuration
{
    public class document_policies: OneOffConfigurationsContext
    {
        private readonly ITestOutputHelper _output;

        public document_policies(ITestOutputHelper output)
        {
            _output = output;

            StoreOptions(_ =>
            {
                _.Schema.For<Target>();
                _.Schema.For<User>().UseOptimisticConcurrency(false);

                _.Policies.ForAllDocuments(m => m.UseOptimisticConcurrency = true);
            });
        }

        [Fact]
        public void applies_to_all_document_types_that_are_not_otherwise_configured()
        {
            theStore.Storage.MappingFor(typeof(Target)).UseOptimisticConcurrency.ShouldBeTrue();
            theStore.Storage.MappingFor(typeof(Issue)).UseOptimisticConcurrency.ShouldBeTrue();
        }

        [Fact]
        public void can_be_overridden_by_explicits()
        {
            theStore.Storage.MappingFor(typeof(User)).UseOptimisticConcurrency.ShouldBeFalse();
        }

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
                #endregion
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
