using System.Linq;
using System.Threading.Tasks;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables.Partitioning;
using Xunit;

namespace CoreTests.Partitioning;

public class partitioning_configuration : OneOffConfigurationsContext
{
    private DocumentTable tableFor<T>()
    {
        var mapping = theStore.Options.Storage.MappingFor(typeof(T));
        return new DocumentTable(mapping);
    }

    [Fact]
    public void configure_for_soft_deleted()
    {
        StoreOptions(opts => opts.Schema.For<Target>().SoftDeletedWithPartitioning());

        var table = tableFor<Target>();

        var partitioning = table.Partitioning.ShouldBeOfType<ListPartitioning>();
        partitioning.Partitions.Single().ShouldBe(new ListPartition("deleted", "TRUE"));
    }

    [Fact]
    public void configure_for_soft_deleted_and_index()
    {
        StoreOptions(opts => opts.Schema.For<Target>().SoftDeletedWithPartitioningAndIndex());

        var table = tableFor<Target>();

        var partitioning = table.Partitioning.ShouldBeOfType<ListPartitioning>();
        partitioning.Partitions.Single().ShouldBe(new ListPartition("deleted", "TRUE"));
    }

    [Fact]
    public void all_documents_are_soft_deleted_and_partitioned()
    {
        StoreOptions(opts => opts.Policies.AllDocumentsSoftDeletedWithPartitioning());

        tableFor<Target>().Partitioning
            .ShouldBeOfType<ListPartitioning>()
            .Partitions
            .Single()
            .ShouldBe(new ListPartition("deleted", "TRUE"));

        tableFor<User>().Partitioning
            .ShouldBeOfType<ListPartitioning>()
            .Partitions
            .Single()
            .ShouldBe(new ListPartition("deleted", "TRUE"));
    }

    [Fact]
    public async Task actually_build_out_partitioned_tables()
    {
        StoreOptions(opts => opts.Schema.For<Target>().SoftDeletedWithPartitioning());

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var tables = await conn.ExistingTablesAsync(schemas: [SchemaName]);

        tables.ShouldContain(new DbObjectName(SchemaName, "mt_doc_target_deleted"));

    }
}
