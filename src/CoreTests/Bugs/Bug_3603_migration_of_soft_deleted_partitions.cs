using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_3603_migration_of_soft_deleted_partitions : BugIntegrationContext
{
    [Fact]
    public async Task do_not_repeatedly_patch()
    {
        // Weasel has been erroneously "finding" deltas when it should not

        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().SoftDeletedWithPartitioningAndIndex();

            opts.Events.UseArchivedStreamPartitioning = true;
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();

        var store2 = SeparateStore(opts =>
        {
            opts.Schema.For<Target>().SoftDeletedWithPartitioningAndIndex();
            opts.Events.UseArchivedStreamPartitioning = true;
        });

        await store2.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}
