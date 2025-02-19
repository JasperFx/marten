using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_621_bulk_insert_with_optimistic_concurrency: BugIntegrationContext
{
    [Fact]
    public async Task can_do_a_bulk_insert()
    {
        var targets = Target.GenerateRandomData(1000).ToArray();

        StoreOptions(_ =>
        {
            _.Schema.For<Target>().UseOptimisticConcurrency(true);
        });

        await theStore.BulkInsertAsync(targets);

        using (var query = theStore.QuerySession())
        {
            query.Query<Target>().Count().ShouldBe(1000);
        }
    }

}
