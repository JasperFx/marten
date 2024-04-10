using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace LinqTests.Bugs;

public class Bug_3116_more_expression_to_constant_issues : BugIntegrationContext
{
    [Fact]
    public async Task query_works()
    {
        int from = 0;
        const int batchSize = 100;

        await theStore.BulkInsertDocumentsAsync(Target.GenerateRandomData(100));

        await theSession.Query<Target>()
            .Where(
                e => e.String == "something"
                     && e.Number >= from
                     && e.Number < from + batchSize)
            .OrderBy(r => r.Number)
            .ToListAsync();
    }
}
