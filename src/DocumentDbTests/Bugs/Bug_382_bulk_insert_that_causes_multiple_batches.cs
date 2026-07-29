using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Weasel.Core;

namespace DocumentDbTests.Bugs;

public class Bug_382_bulk_insert_that_causes_multiple_batches: BugIntegrationContext
{
    [Fact]
    public async Task load_with_batch_larger_than_batch_size_and_overwrite_existing_on_empty_database()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Target>().Duplicate(x => x.Date);
        });

        // BugIntegrationContext pins every bug test to one shared "bugs" schema, and CI runs
        // several suites against the same database in sequence, so a global Target count can
        // pick up rows another test wrote. See #5070.
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));

        var data = Target.GenerateRandomData(11).ToArray();

        await theStore.BulkInsertAsync(data, BulkInsertMode.OverwriteExisting, batchSize: 10);

        (await theSession.Query<Target>().CountAsync()).ShouldBe(data.Length);
    }


}
