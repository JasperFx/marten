using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Schema
{
    public class BulkLoaderConstructionTests : IntegrationContext
    {
        [Fact]
        public void can_build_a_bulk_loader_with_searchable_fields()
        {
            StoreOptions(_ => _.Schema.For<Target>().Duplicate(x => x.Number).Duplicate(x => x.StringField));


            var loader = theStore.Tenancy.Default.BulkLoaderFor<Target>();

            loader.ShouldNotBeNull();
        }

        public BulkLoaderConstructionTests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
