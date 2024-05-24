using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Xunit;

namespace CoreTests;

public class migrate_from_guid_to_int_based_revisions
{
    [Fact]
    public async Task automatic_conversion_of_guid_version_to_integer()
    {
        using var store1 = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "migrations";

            opts.Schema.For<MigratedDoc>().UseOptimisticConcurrency(true);
        });

        await store1.Advanced.Clean.CompletelyRemoveAllAsync();

        await store1.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        using var store2 = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "migrations";

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
