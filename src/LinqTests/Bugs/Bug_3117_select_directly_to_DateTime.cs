using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace LinqTests.Bugs;

public class Bug_3117_select_directly_to_DateTime : BugIntegrationContext
{
    [Fact]
    public async Task select_max_date_time()
    {
        await theStore.BulkInsertDocumentsAsync(Target.GenerateRandomData(100));

        var time = await theSession.Query<Target>().MaxAsync(x => x.Date);
    }
}
