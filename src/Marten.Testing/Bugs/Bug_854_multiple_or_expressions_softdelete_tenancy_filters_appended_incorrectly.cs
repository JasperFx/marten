using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Testing.Linq;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_854_multiple_or_expressions_softdelete_tenancy_filters_appended_incorrectly: BugIntegrationContext
    {
        [Fact]
        public void query_where_with_multiple_or_expressions_against_single_tenant()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().MultiTenanted();
            });

            Target[] reds = Target.GenerateRandomData(50).ToArray();

            theStore.BulkInsert("Bug_854", reds);

            var expected = reds.Where(x => x.String == "Red" || x.String == "Orange").Select(x => x.Id).OrderBy(x => x).ToArray();

            using (var query = theStore.QuerySession("Bug_854"))
            {
                var actual = query.Query<Target>().Where(x => x.String == "Red" || x.String == "Orange")
                                  .OrderBy(x => x.Id).Select(x => x.Id).ToArray();

                actual.ShouldHaveTheSameElementsAs(expected);
            }
        }

        [Fact]
        public void query_where_with_multiple_or_expresions_against_soft_Deletes()
        {
            StoreOptions(_ => _.Schema.For<SoftDeletedItem>().SoftDeleted());

            var item1 = new SoftDeletedItem { Number = 1, Name = "Jim Bob" };
            var item2 = new SoftDeletedItem { Number = 2, Name = "Joe Bill" };
            var item3 = new SoftDeletedItem { Number = 1, Name = "Jim Beam" };

            int expected = 3;

            using (var session = theStore.OpenSession())
            {
                session.Store(item1, item2, item3);
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession())
            {
                var query = session.Query<SoftDeletedItem>()
                    .Where(x => x.Number == 1 || x.Number == 2);

                var actual = query.ToList().Count;
                Assert.Equal(expected, actual);
            }
        }

    }
}
