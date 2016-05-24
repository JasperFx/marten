using System;
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
        }
    }
}