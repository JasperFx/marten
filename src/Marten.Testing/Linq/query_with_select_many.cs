using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Util;
using Remotion.Linq.Clauses;
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

        [Fact]
        public async Task select_many_against_integer_array_async()
        {
            var product1 = new ProductWithNumbers() { Tags = new[] { 1, 2, 3 } };
            var product2 = new ProductWithNumbers { Tags = new[] { 2, 3, 4 } };
            var product3 = new ProductWithNumbers { Tags = new[] { 3, 4, 5 } };

            using (var session = theStore.OpenSession())
            {
                session.Store(product1, product2, product3);
                await session.SaveChangesAsync();
            }


            using (var query = theStore.QuerySession())
            {
                var distinct = await query.Query<ProductWithNumbers>().SelectMany(x => x.Tags).Distinct().ToListAsync();

                distinct.OrderBy(x => x).ShouldHaveTheSameElementsAs(1, 2, 3, 4, 5);

                var names = query.Query<ProductWithNumbers>().SelectMany(x => x.Tags).ToList();
                names
                    .Count().ShouldBe(9);
            }
        }

        [Fact]
        public void select_many_against_complex_type_without_transformation()
        {
            var targets = Target.GenerateRandomData(10).ToArray();
            var expectedCount = targets.SelectMany(x => x.Children).Count();

            expectedCount.ShouldBeGreaterThan(0);


            using (var session = theStore.OpenSession())
            {
                session.Store(targets);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                var list = query.Query<Target>().SelectMany(x => x.Children).ToList();
                list.Count.ShouldBe(expectedCount);
            }
        }

        [Fact]
        public void select_many_against_complex_type_with_count()
        {
            var product1 = new Product { Tags = new[] { "a", "b", "c" } };
            var product2 = new Product { Tags = new[] { "b", "c", "d" } };
            var product3 = new Product { Tags = new[] { "d", "e", "f" } };

            using (var session = theStore.OpenSession())
            {
                session.Store(product1, product2, product3);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                query.Query<Product>().SelectMany(x => x.Tags)
                    .Count().ShouldBe(9);

            }
        }

        [Fact]
        public async Task select_many_against_complex_type_with_count_async()
        {
            var product1 = new Product { Tags = new[] { "a", "b", "c" } };
            var product2 = new Product { Tags = new[] { "b", "c", "d" } };
            var product3 = new Product { Tags = new[] { "d", "e", "f" } };

            using (var session = theStore.OpenSession())
            {
                session.Store(product1, product2, product3);
                await session.SaveChangesAsync();
            }

            using (var query = theStore.QuerySession())
            {
                (await query.Query<Product>().SelectMany(x => x.Tags)
                    .CountAsync()).ShouldBe(9);

            }
        }

        [Fact]
        public void select_many_with_any()
        {
            var product1 = new Product { Tags = new[] { "a", "b", "c" } };
            var product2 = new Product { Tags = new[] { "b", "c", "d" } };
            var product3 = new Product { Tags = new[] { "d", "e", "f" } };

            using (var session = theStore.OpenSession())
            {
                session.Store(product1, product2, product3);

                // Some Target docs w/ no children
                session.Store(Target.Random(), Target.Random(), Target.Random());

                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                query.Query<Product>().SelectMany(x => x.Tags)
                    .Any().ShouldBeTrue();

                query.Query<Target>().SelectMany(x => x.Children)
                    .Any().ShouldBeFalse();


            }
        }

        [Fact]
        public async Task select_many_with_any_async()
        {
            var product1 = new Product { Tags = new[] { "a", "b", "c" } };
            var product2 = new Product { Tags = new[] { "b", "c", "d" } };
            var product3 = new Product { Tags = new[] { "d", "e", "f" } };

            using (var session = theStore.OpenSession())
            {
                session.Store(product1, product2, product3);

                // Some Target docs w/ no children
                session.Store(Target.Random(), Target.Random(), Target.Random());

                await session.SaveChangesAsync().ConfigureAwait(false);
            }

            using (var query = theStore.QuerySession())
            {
                (await query.Query<Product>().SelectMany(x => x.Tags)
                    .AnyAsync()).ShouldBeTrue();

                (await query.Query<Target>().SelectMany(x => x.Children)
                    .AnyAsync()).ShouldBeFalse();


            }
        }



        [Fact]
        public void playing()
        {
            using (var query = theStore.QuerySession())
            {
                var targets = query.Query<Target>()
                    .Where(x => x.Flag)
                    .Where(x => x.Decimal > 5)
                    .SelectMany(x => x.Children)
                    .Where(x => x.Flag)
                    .OrderBy(x => x.Color)
                    .ToList();
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