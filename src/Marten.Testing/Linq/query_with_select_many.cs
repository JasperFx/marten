using System;
using System.Linq;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class query_with_select_many : IntegratedFixture
    {
        [Fact]
        public void can_do_simple_select_many_against_simple_array()
        {
            var product1 = new Product {Tags = new [] {"a", "b", "c"}};
            var product2 = new Product {Tags = new [] {"b", "c", "d"}};
            var product3 = new Product {Tags = new [] {"d", "e", "f"}};

            using (var session = theStore.OpenSession())
            {
                session.Store(product1, product2, product3);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                var names = query.Query<Product>().SelectMany(x => x.Tags).ToList();

                names.Count.ShouldBe(6);
            }
        }
    }

    public class Product
    {
        public Guid Id;
        public string[] Tags { get; set; }

    }
}