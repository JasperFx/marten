using System.Linq;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Storage;
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

    [Fact]
    public async Task partitioned_by_tenant_id_to_partitioned_to_tenant_id_and_tenant_id_is_sorted_first()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Issue>()
                .ForeignKey<User>(x => x.AssigneeId);

            opts.Schema.For<User>();

            opts.Policies.AllDocumentsAreMultiTenantedWithPartitioning(partitioning =>
            {
                partitioning.ByHash("one", "two", "three");
            });
        });

        // Just smoke test that it works
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();

        var mapping = (DocumentMapping)theStore.Options.Storage.FindMapping(typeof(Issue));
        var table = new DocumentTable(mapping);

        var fk = table.ForeignKeys.Single();
        fk.ColumnNames.ShouldBe(["tenant_id", "assignee_id"]);
        fk.LinkedNames.ShouldBe(["tenant_id", "id"]);

        fk.Name.ShouldBe("mt_doc_issue_tenant_id_assignee_id_fkey");
    }
}
