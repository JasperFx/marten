using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Marten.Testing.Linq
{
    public class query_with_multiple_where_clauses_and_soft_deletes_configured : IntegratedFixture
    {
        [Fact]
        public async Task TestMultipleWhereClausesWithSoftDeleteConfigured()
        {
            StoreOptions(_ => _.Schema.For<SoftDeletedItem>().SoftDeleted());

            var item1 = new SoftDeletedItem { Number = 1, Name = "Jim Bob" };
            var item2 = new SoftDeletedItem { Number = 2, Name = "Joe Bill" };
            var item3 = new SoftDeletedItem { Number = 1, Name = "Jim Beam" };

            using (var session = theStore.OpenSession())
            {
                session.Store(item1, item2, item3);
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                var query = session.Query<SoftDeletedItem>()
                    .Where(x => x.Number == 1)
                    .Where(x => x.Name.StartsWith("Jim"));

                var result = await query.ToListAsync();
                Assert.Equal(2, result.Count);
            }
        }

        [Fact]
        public async Task TestMultipleWhereClausesWithoutSoftDeleteConfigured()
        {
            StoreOptions(_ => _.Schema.For<SoftDeletedItem>());

            var item1 = new SoftDeletedItem { Number = 1, Name = "Jim Bob" };
            var item2 = new SoftDeletedItem { Number = 2, Name = "Joe Bill" };
            var item3 = new SoftDeletedItem { Number = 1, Name = "Jim Beam" };

            using (var session = theStore.OpenSession())
            {
                session.Store(item1, item2, item3);
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                var query = session.Query<SoftDeletedItem>()
                    .Where(x => x.Number == 1)
                    .Where(x => x.Name.StartsWith("Jim"));

                var result = await query.ToListAsync();
                Assert.Equal(2, result.Count);
            }
        }
    }

    public class SoftDeletedItem
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public string Name { get; set; }
    }

}
