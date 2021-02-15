using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Marten.Testing.Linq;
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

            theSession.MetadataFor(shop)
                .ShouldBeNull();

        }

        #region sample_resolving_metadata
        [Fact]
        public void hit_returns_values()
        {
            var shop = new CoffeeShop();

            using (var session = theStore.OpenSession())
            {
                session.Store(shop);
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                var metadata = session.MetadataFor(shop);

                metadata.ShouldNotBeNull();
                metadata.CurrentVersion.ShouldNotBe(Guid.Empty);
                metadata.LastModified.ShouldNotBe(default);
                metadata.DotNetType.ShouldBe(typeof(CoffeeShop).FullName);
                metadata.DocumentType.ShouldBeNull();
                metadata.Deleted.ShouldBeFalse();
                metadata.DeletedAt.ShouldBeNull();
            }


        }

        #endregion sample_resolving_metadata

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
                await session.SaveChangesAsync();

                session.Delete(shop);
                await session.SaveChangesAsync();
            }

            using var query = theStore.QuerySession();
            var metadata = await query.MetadataForAsync(shop);

            metadata.ShouldNotBeNull();
            metadata.CurrentVersion.ShouldNotBe(Guid.Empty);
            metadata.LastModified.ShouldNotBe(default);
            metadata.DotNetType.ShouldBe(typeof(CoffeeShop).FullName);
            metadata.DocumentType.ShouldBe("coffee_shop");
            metadata.Deleted.ShouldBeTrue();
            metadata.DeletedAt.ShouldNotBeNull();
        }

        public fetching_entity_metadata(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
