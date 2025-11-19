using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace CoreTests.Partitioning;

// Spawned by https://github.com/JasperFx/marten/issues/4025
public class partitioning_by_hash_on_id : OneOffConfigurationsContext
{
    [Fact]
    public async Task try_it_out()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<LongDoc>()
                .PartitionOn(x => x.Id, x => x.ByHash("one", "two", "three"));
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }
}
