using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace CoreTests.Partitioning;

public class partitioning_migrations : OneOffConfigurationsContext
{
    [Fact]
    public async Task should_create_delta_for_adding_partitioning_period()
    {
        StoreOptions(opts => opts.Schema.For<Target>().MultiTenanted());

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(opts =>
        {
            opts.Schema.For<Target>().MultiTenantedWithPartitioning(x => x.ByRange()
                .AddRange("one", "t1", "t2")
                .AddRange("two", "t3", "t4"));
        });

        var migration = await store2.Storage.CreateMigrationAsync();
        migration.Difference.ShouldBe(SchemaPatchDifference.Update);
    }

    [Fact]
    public async Task should_create_delta_for_adding_all_new_partitions()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenantedWithPartitioning(x => x.ByRange()
                .AddRange("one", "t1", "t2")
                .AddRange("two", "t3", "t4"));
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(opts =>
        {
            opts.Schema.For<Target>().MultiTenantedWithPartitioning(x => x.ByRange()
                .AddRange("one", "t1", "t2")
                .AddRange("two", "t3", "t4")
                .AddRange("three", "t5", "t6")

            );
        });

        var migration = await store2.Storage.CreateMigrationAsync();
        migration.Difference.ShouldBe(SchemaPatchDifference.Update);
    }

    [Fact]
    public async Task partitioning_with_soft_deletes_multiple_migrations()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(opts =>
        {
            opts.Schema.For<Target>().SoftDeletedWithPartitioning();
        });

        await store2.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store3 = SeparateStore(opts =>
        {
            opts.Schema.For<Target>().SoftDeletedWithPartitioning();
        });

        await store3.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}
