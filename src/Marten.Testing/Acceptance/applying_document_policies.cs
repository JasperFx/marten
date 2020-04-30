using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class applying_document_policies: IntegrationContext
    {
        public applying_document_policies(DefaultStoreFixture fixture) : base(fixture)
        {
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
    }
}
