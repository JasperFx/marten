using System.Diagnostics;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace CoreTests.Bugs;

public class Bug_2914_NGram_index_being_changed_incorrectly : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_2914_NGram_index_being_changed_incorrectly(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task recognize_when_it_is_correct()
    {
        StoreOptions(opts =>
        {
            opts.Logger(new TestOutputMartenLogger(_output));
            opts.Schema.For<NGramDoc>().NgramIndex(x => x.NGramString);
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(NGramDoc));

        var store = SeparateStore(opts =>
        {
            opts.Logger(new TestOutputMartenLogger(_output));
            opts.Schema.For<NGramDoc>().NgramIndex(x => x.NGramString);
        });

        await store.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}
