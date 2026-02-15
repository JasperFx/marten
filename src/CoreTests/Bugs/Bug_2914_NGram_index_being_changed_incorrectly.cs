using System.Threading.Tasks;
using Marten.Testing.Harness;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_2914_NGram_index_being_changed_incorrectly : BugIntegrationContext
{
    [Fact]
    public async Task recognize_when_it_is_correct()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<NGramDoc>().NgramIndex(x => x.NGramString);
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(NGramDoc));

        var store = SeparateStore(opts =>
        {
            opts.Schema.For<NGramDoc>().NgramIndex(x => x.NGramString);
        });

        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}
