using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_2850_missed_field_reducing : BugIntegrationContext
{
    public async Task RunQuery(bool include, int resultCount)
    {
        var results = await theSession.Query<Target>().Where(x => include || !x.Flag).CountAsync();
        results.ShouldBe(resultCount);
    }

    [Fact]
    public async Task pass_bool_into_query()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));

        var targets = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(targets);

        var count = targets.Count(x => x.Flag);

        await RunQuery(true, 100);
        await RunQuery(false, 100 - count);
    }
}
