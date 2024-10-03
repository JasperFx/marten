using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.Tables.Partitioning;
using Xunit;

namespace CoreTests.Partitioning;

public class partitioning_documents_on_duplicate_fields : OneOffConfigurationsContext
{
    [Fact]
    public async Task use_duplicated_fields_as_partitioning()
    {
        StoreOptions(opts => opts.Schema.For<Target>().PartitionOn(x => x.Number, x =>
        {
            x.ByRange()
                .AddRange("low", 0, 10)
                .AddRange("high", 11, 1000);
        }));

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var table = new DocumentTable(theStore.Options.Storage.MappingFor(typeof(Target)));
        table.Partitioning.ShouldBeOfType<RangePartitioning>();

        await theStore.BulkInsertAsync(Target.GenerateRandomData(100).ToArray());
    }

    public static void configure_partitioning()
    {
        #region sample_configuring_partitioning_by_document_member

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Set up table partitioning for the User document type
            opts.Schema.For<User>()
                .PartitionOn(x => x.Age, x =>
                {
                    x.ByRange()
                        .AddRange("young", 0, 20)
                        .AddRange("twenties", 21, 29)
                        .AddRange("thirties", 31, 39);
                });

            // Or use pg_partman to manage partitioning outside of Marten
            opts.Schema.For<User>()
                .PartitionOn(x => x.Age, x =>
                {
                    x.ByExternallyManagedRangePartitions();

                    // or instead with list

                    x.ByExternallyManagedListPartitions();
                });

            // Or use PostgreSQL HASH partitioning and split the users over multiple tables
            opts.Schema.For<User>()
                .PartitionOn(x => x.UserName, x =>
                {
                    x.ByHash("one", "two", "three");
                });

            opts.Schema.For<Issue>()
                .PartitionOn(x => x.Status, x =>
                {
                    // There is a default partition for anything that doesn't fall into
                    // these specific values
                    x.ByList()
                        .AddPartition("completed", "Completed")
                        .AddPartition("new", "New");
                });

        });

        #endregion
    }
}
