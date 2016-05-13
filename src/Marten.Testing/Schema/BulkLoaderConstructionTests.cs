using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
using Xunit;

namespace Marten.Testing.Schema
{
    public class BulkLoaderConstructionTests : IntegratedFixture
    {
        [Fact]
        public void can_build_a_bulk_loader_with_searchable_fields()
        {
            StoreOptions(_ => _.Schema.For<Target>().Searchable(x => x.Number).Searchable(x => x.StringField));


            var loader = theStore.Schema.BulkLoaderFor<Target>();

            loader.ShouldNotBeNull();
        }
    }
}