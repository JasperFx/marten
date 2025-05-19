using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

public class Bug_1404_Hilo_concurrent_update_failure : BugIntegrationContext
{
    private async Task Hammertime()
    {
        var store = SeparateStore(opts =>
        {
            opts.Advanced.HiloSequenceDefaults.MaxLo = 5;
        });

        await store.BulkInsertAsync( TargetIntId.GenerateRandomData(100).ToArray());
    }

    [Fact]
    public async Task generate_hilo_in_highly_concurrent_scenarios()
    {
        // ensure we create required DB objects since the concurrent
        // test could potentially create the same DB objects at the same time
        var store = StoreOptions(opts =>
        {
            opts.Advanced.HiloSequenceDefaults.MaxLo = 5;
        });

        await store.BulkInsertAsync( TargetIntId.GenerateRandomData(100).ToArray());

        await Task.WhenAll(Task.Run(Hammertime), Task.Run(Hammertime), Task.Run(Hammertime),
            Task.Run(Hammertime));
    }
}
