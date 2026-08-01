using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Xunit;

namespace CoreTests;

public class migrate_from_guid_to_int_based_revisions: OneOffConfigurationsContext
{
    [Fact]
    public async Task automatic_conversion_of_guid_version_to_integer()
    {
        var store1 = StoreOptions(opts =>
        {
            opts.Schema.For<MigratedDoc>().UseOptimisticConcurrency(true);
        });

        await store1.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(opts =>
        {
            opts.Schema.For<MigratedDoc>().UseNumericRevisions(true);
        });

        await store2.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await store2.Storage.Database.AssertDatabaseMatchesConfigurationAsync();

    }
}

public class MigratedDoc
{
    public Guid Id { get; set; }
}
