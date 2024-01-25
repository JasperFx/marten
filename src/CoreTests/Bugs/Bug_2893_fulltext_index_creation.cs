using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_2893_fulltext_index_creation : BugIntegrationContext
{
    [Fact]
    public async Task can_create_index_and_not_have_to_recreate_every_time()
    {
        StoreOptions(opts => opts.Schema.For<Target>().FullTextIndex());

        await theStore.BulkInsertDocumentsAsync(Target.GenerateRandomData(100).ToArray());

        var store = SeparateStore(opts => opts.Schema.For<Target>().FullTextIndex());

        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}

