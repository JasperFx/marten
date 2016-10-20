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

            using (var conn = theStore.Advanced.OpenConnection())
            {
                conn.Execute(c =>
                {
                    var sql = @"select distinct jsonb_array_elements(data -> 'Tags') as x
from mt_doc_product
order by x;";

                    var reader = c.Sql(sql).ExecuteReader();
                    while (reader.Read())
                    {
                        Console.WriteLine(reader.GetString(0));
                    }
                });
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
    }

    public class Product
    {
        public Guid Id;
        public string[] Tags { get; set; }

    }
}