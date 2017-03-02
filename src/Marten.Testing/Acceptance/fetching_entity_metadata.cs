using System;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class fetching_entity_metadata : IntegratedFixture
    {
        [Fact]
        public void total_miss_returns_null()
        {
            var shop = new CoffeeShop();

            theStore.Advanced.MetadataFor(shop)
                .ShouldBeNull();
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

            var metadata = theStore.Advanced.MetadataFor(shop);

            metadata.ShouldNotBeNull();
            metadata.CurrentVersion.ShouldNotBe(Guid.Empty);
            metadata.LastModified.ShouldNotBe(default(DateTime));
            metadata.DotNetType.ShouldBe(typeof(CoffeeShop).FullName);
            metadata.DocumentType.ShouldBeNull();
            metadata.Deleted.ShouldBeFalse();
            metadata.DeletedAt.ShouldBeNull();
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

            var metadata = await theStore.Advanced.MetadataForAsync(shop);

            metadata.ShouldNotBeNull();
            metadata.CurrentVersion.ShouldNotBe(Guid.Empty);
            metadata.LastModified.ShouldNotBe(default(DateTime));
            metadata.DotNetType.ShouldBe(typeof(CoffeeShop).FullName);
            metadata.DocumentType.ShouldBe("coffee_shop");
            metadata.Deleted.ShouldBeTrue();
            metadata.DeletedAt.ShouldNotBeNull();
        }
    }
}
