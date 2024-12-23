using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.Tables;
using Xunit;

namespace CoreTests.Partitioning;

public class partitioning_and_foreign_keys : OneOffConfigurationsContext
{
    /*
     * Partitioned to partitioned
     * Not partitioned to partitioned
     *
     *
     *
     */

    [Fact]
    public async Task from_partitioned_to_not_partitioned()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Issue>()
                .SoftDeletedWithPartitioningAndIndex()
                .ForeignKey<User>(x => x.AssigneeId);
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task from_partitioned_to_partitioned()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Issue>()
                .SoftDeletedWithPartitioningAndIndex()
                .ForeignKey<User>(x => x.AssigneeId);

            opts.Schema.For<User>().SoftDeletedWithPartitioningAndIndex();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task from_not_partitioned_to_partitioned()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Issue>()
                .ForeignKey<User>(x => x.AssigneeId);

            opts.Schema.For<User>().SoftDeletedWithPartitioningAndIndex();
        });

        await Should.ThrowAsync<InvalidForeignKeyException>(async () =>
        {
            await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        });
    }
}
