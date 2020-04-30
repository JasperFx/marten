using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class fetching_entity_metadata: IntegrationContext
    {
        [Fact]
        public void total_miss_returns_null()
        {
            var shop = new CoffeeShop();

            SpecificationExtensions.ShouldBeNull(theStore.Tenancy.Default.MetadataFor(shop));
        }

        // SAMPLE: resolving_metadata
        [Fact]
        public void hit_returns_values()
        {
            var shop = new CoffeeShop();

            using (var session = theStore.OpenSession())
            {
                session.Store(shop);
                session.SaveChanges();
            }

            var metadata = theStore.Tenancy.Default.MetadataFor(shop);

            SpecificationExtensions.ShouldNotBeNull(metadata);
            metadata.CurrentVersion.ShouldNotBe(Guid.Empty);
            metadata.LastModified.ShouldNotBe(default(DateTime));
            metadata.DotNetType.ShouldBe(typeof(CoffeeShop).FullName);
            SpecificationExtensions.ShouldBeNull(metadata.DocumentType);
            metadata.Deleted.ShouldBeFalse();
            SpecificationExtensions.ShouldBeNull(metadata.DeletedAt);
        }

        // ENDSAMPLE

        [Fact]
        public async Task async_hit_returns_values()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Shop>().SoftDeleted().AddSubClass<CoffeeShop>();
            });

            var shop = new CoffeeShop();

            using (var session = theStore.OpenSession())
            {
                session.Store(shop);
                session.SaveChanges();

                session.Delete(shop);
                session.SaveChanges();
            }

            var metadata = await theStore.Tenancy.Default.MetadataForAsync(shop);

            SpecificationExtensions.ShouldNotBeNull(metadata);
            metadata.CurrentVersion.ShouldNotBe(Guid.Empty);
            metadata.LastModified.ShouldNotBe(default(DateTime));
            metadata.DotNetType.ShouldBe(typeof(CoffeeShop).FullName);
            metadata.DocumentType.ShouldBe("coffee_shop");
            metadata.Deleted.ShouldBeTrue();
            SpecificationExtensions.ShouldNotBeNull(metadata.DeletedAt);
        }

        public fetching_entity_metadata(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
