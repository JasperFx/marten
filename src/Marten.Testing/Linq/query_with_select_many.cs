using System;
using System.Linq;
using Marten.Util;
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
                var distinct = query.Query<Product>().SelectMany(x => x.Tags).Distinct().ToList();

                distinct.OrderBy(x => x).ShouldHaveTheSameElementsAs("a", "b", "c", "d", "e", "f");

                var names = query.Query<Product>().SelectMany(x => x.Tags).ToList();
                names
                    .Count().ShouldBe(9);
            }

        }

        [Fact]
        public void select_many_against_integer_array()
        {
            var product1 = new ProductWithNumbers() { Tags = new[] { 1,2,3 } };
            var product2 = new ProductWithNumbers { Tags = new[] { 2,3,4 } };
            var product3 = new ProductWithNumbers { Tags = new[] { 3, 4,5 } };

            using (var session = theStore.OpenSession())
            {
                session.Store(product1, product2, product3);
                session.SaveChanges();
            }


            using (var query = theStore.QuerySession())
            {
                var distinct = query.Query<ProductWithNumbers>().SelectMany(x => x.Tags).Distinct().ToList();

                distinct.OrderBy(x => x).ShouldHaveTheSameElementsAs(1, 2, 3, 4, 5);

                var names = query.Query<ProductWithNumbers>().SelectMany(x => x.Tags).ToList();
                names
                    .Count().ShouldBe(9);
            }
        }
    }

    public class Product
    {
        public Guid Id;
        public string[] Tags { get; set; }

    }

    public class ProductWithNumbers
    {
        public Guid Id;
        public int[] Tags { get; set; }

    }
}