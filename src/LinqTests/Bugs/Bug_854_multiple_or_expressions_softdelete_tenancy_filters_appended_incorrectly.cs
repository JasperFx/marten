using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace LinqTests.Bugs;

public class SoftDeletedItem
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Name { get; set; }
}

public class Bug_854_multiple_or_expressions_softdelete_tenancy_filters_appended_incorrectly: BugIntegrationContext
{
    [Fact]
    public async Task query_where_with_multiple_or_expressions_against_single_tenant()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Target>().MultiTenanted();
        });

        Target[] reds = Target.GenerateRandomData(50).ToArray();

        await theStore.BulkInsertAsync("Bug_854", reds);

        var expected = reds.Where(x => x.String == "Red" || x.String == "Orange").Select(x => x.Id).OrderBy(x => x).ToArray();

        using (var query = theStore.QuerySession("Bug_854"))
        {
            var actual = query.Query<Target>().Where(x => x.String == "Red" || x.String == "Orange")
                .OrderBy(x => x.Id).Select(x => x.Id).ToArray();

            actual.ShouldHaveTheSameElementsAs(expected);
        }
    }

    [Fact]
    public async Task query_where_with_multiple_or_expresions_against_soft_Deletes()
    {
        StoreOptions(_ => _.Schema.For<SoftDeletedItem>().SoftDeleted());

        var item1 = new SoftDeletedItem { Number = 1, Name = "Jim Bob" };
        var item2 = new SoftDeletedItem { Number = 2, Name = "Joe Bill" };
        var item3 = new SoftDeletedItem { Number = 1, Name = "Jim Beam" };

        var expected = 3;

        using (var session = theStore.LightweightSession())
        {
            session.Store(item1, item2, item3);
            await session.SaveChangesAsync();
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
