using System.Linq;
using System.Threading.Tasks;
using Marten.Storage;
using Marten.Storage.Metadata;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables.Partitioning;
using Xunit;
using Xunit.Abstractions;

namespace CoreTests.Partitioning;

public class partitioning_configuration : OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public partitioning_configuration(ITestOutputHelper output)
    {
        _output = output;
    }

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
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().SoftDeletedWithPartitioning();
            opts.Logger(new TestOutputMartenLogger(_output));
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var tables = await conn.ExistingTablesAsync(schemas: [SchemaName]);

        tables.ShouldContain(new DbObjectName(SchemaName, "mt_doc_target_deleted"));

    }

    [Fact]
    public void configure_hash_partitioning_on_tenant_id()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenantedWithPartitioning(x => x.ByHash("one", "two", "three"));
        });

        var table = tableFor<Target>();
        var partitioning = table.Partitioning.ShouldBeOfType<HashPartitioning>();
        partitioning.Columns.Single().ShouldBe(TenantIdColumn.Name);
        partitioning.Suffixes.ShouldBe(["one", "two", "three"]);
    }

    [Fact]
    public void configure_list_partitioning_on_tenant_id()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenantedWithPartitioning(x => x.ByList()
                .AddPartition("one", "t1", "t2")
                .AddPartition("two", "t3", "t4"));


        });

        var table = tableFor<Target>();
        var partitioning = table.Partitioning.ShouldBeOfType<ListPartitioning>();
        partitioning.Columns.Single().ShouldBe(TenantIdColumn.Name);
        partitioning.Partitions[0].Values.ShouldBe(["'t1'", "'t2'"]);
    }

    [Fact]
    public void configure_list_partitioning_with_external_managed_partitioins_on_tenant_id()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenantedWithPartitioning(x => x.ByExternallyManagedListPartitions());
        });

        var table = tableFor<Target>();
        var partitioning = table.Partitioning.ShouldBeOfType<ListPartitioning>();
        partitioning.Columns.Single().ShouldBe(TenantIdColumn.Name);
        table.IgnorePartitionsInMigration.ShouldBeTrue();
    }

    [Fact]
    public void configure_list_partitioning_with_external_managed_partitioins_on_tenant_id_with_global_policy()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenantedWithPartitioning(x => x.ByExternallyManagedListPartitions());
        });

        var table = tableFor<Target>();
        var partitioning = table.Partitioning.ShouldBeOfType<ListPartitioning>();
        partitioning.Columns.Single().ShouldBe(TenantIdColumn.Name);
        table.IgnorePartitionsInMigration.ShouldBeTrue();
    }

    [Fact]
    public void configure_list_partitioning_with_external_managed_partitioins_on_by_selective_policy()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
            opts.Policies.PartitionMultiTenantedDocuments(x => x.ByExternallyManagedListPartitions());
        });

        var table = tableFor<Target>();
        var partitioning = table.Partitioning.ShouldBeOfType<ListPartitioning>();
        partitioning.Columns.Single().ShouldBe(TenantIdColumn.Name);
        table.IgnorePartitionsInMigration.ShouldBeTrue();
    }

    [Fact]
    public void configure_range_partitioning_with_external_managed_partitioins_on_tenant_id()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenantedWithPartitioning(x => x.ByExternallyManagedRangePartitions());
        });

        var table = tableFor<Target>();
        var partitioning = table.Partitioning.ShouldBeOfType<RangePartitioning>();
        partitioning.Columns.Single().ShouldBe(TenantIdColumn.Name);
        table.IgnorePartitionsInMigration.ShouldBeTrue();
    }

    [Fact]
    public void configure_range_partitioning_on_tenant_id()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenantedWithPartitioning(x => x.ByRange()
                .AddRange("one", "t1", "t2")
                .AddRange("two", "t3", "t4"));
        });

        var table = tableFor<Target>();
        var partitioning = table.Partitioning.ShouldBeOfType<RangePartitioning>();
        partitioning.Columns.Single().ShouldBe(TenantIdColumn.Name);
        partitioning.Ranges[0].From.ShouldBe("'t1'");
        partitioning.Ranges[0].To.ShouldBe("'t2'");
    }
}
