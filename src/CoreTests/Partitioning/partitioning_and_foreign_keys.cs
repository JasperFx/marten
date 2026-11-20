using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Partitioning;
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

    // marten#6092: Issue references User, and both are partitioned per tenant by Marten management
    private void configureManagedPartitionsWithForeignKey(StoreOptions opts)
    {
        opts.Policies.AllDocumentsAreMultiTenanted();
        opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement(SchemaName);

        opts.Schema.For<User>();
        opts.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId);
    }

    private async Task storeUserAndIssueAsync(string tenantId)
    {
        var user = new User { Id = Guid.NewGuid(), UserName = tenantId };
        var issue = new Issue { Id = Guid.NewGuid(), Title = tenantId, AssigneeId = user.Id };

        await using var session = theStore.LightweightSession(tenantId);
        session.Store(user);
        session.Store(issue);
        await session.SaveChangesAsync();
    }

    private async Task assertOnlyBlueIsLeftAsync()
    {
        var userTable = await theStore.Storage.Database.ExistingTableFor(typeof(User));
        userTable.Partitioning.ShouldBeOfType<ListPartitioning>().Partitions.Select(x => x.Suffix).ShouldBe(["blue"]);

        var issueTable = await theStore.Storage.Database.ExistingTableFor(typeof(Issue));
        issueTable.Partitioning.ShouldBeOfType<ListPartitioning>().Partitions.Select(x => x.Suffix).ShouldBe(["blue"]);

        // The surviving tenant keeps its rows
        await using var blue = theStore.QuerySession("blue");
        (await blue.Query<User>().CountAsync()).ShouldBe(1);
        (await blue.Query<Issue>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task remove_managed_tenant_with_a_foreign_key_between_partitioned_documents()
    {
        StoreOptions(configureManagedPartitionsWithForeignKey);

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "red", "blue");

        await storeUserAndIssueAsync("red");
        await storeUserAndIssueAsync("blue");

        await theStore.Advanced.RemoveMartenManagedTenantsAsync(["red"], CancellationToken.None);

        await assertOnlyBlueIsLeftAsync();
    }

    [Fact]
    public async Task delete_all_tenant_data_with_a_foreign_key_between_managed_partitioned_documents()
    {
        StoreOptions(configureManagedPartitionsWithForeignKey);

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "red", "blue");

        await storeUserAndIssueAsync("red");
        await storeUserAndIssueAsync("blue");

        await theStore.Advanced.DeleteAllTenantDataAsync("red", CancellationToken.None);

        await assertOnlyBlueIsLeftAsync();
    }

    // marten#6096: a document with a foreign key to its own type, partitioned per tenant by Marten management.
    // PostgreSQL 17 refuses to detach such a partition while its rows reference each other (weasel#895).
    private void configureManagedPartitionsWithSelfReference(StoreOptions opts)
    {
        opts.Policies.AllDocumentsAreMultiTenanted();
        opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement(SchemaName);

        opts.Schema.For<TenantTreeNode>().ForeignKey<TenantTreeNode>(x => x.ParentId);
    }

    private async Task storeTreeAsync(string tenantId)
    {
        var root = new TenantTreeNode { Id = Guid.NewGuid(), Name = tenantId };
        var child = new TenantTreeNode { Id = Guid.NewGuid(), Name = tenantId + "-child", ParentId = root.Id };

        await using var session = theStore.LightweightSession(tenantId);
        session.Store(root);
        await session.SaveChangesAsync();

        session.Store(child);
        await session.SaveChangesAsync();
    }

    private async Task assertOnlyBlueTreeIsLeftAsync()
    {
        var table = await theStore.Storage.Database.ExistingTableFor(typeof(TenantTreeNode));
        table.Partitioning.ShouldBeOfType<ListPartitioning>().Partitions.Select(x => x.Suffix).ShouldBe(["blue"]);

        await using var blue = theStore.QuerySession("blue");
        (await blue.Query<TenantTreeNode>().CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task remove_managed_tenant_with_a_self_referencing_partitioned_document()
    {
        StoreOptions(configureManagedPartitionsWithSelfReference);

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "red", "blue");

        await storeTreeAsync("red");
        await storeTreeAsync("blue");

        await theStore.Advanced.RemoveMartenManagedTenantsAsync(["red"], CancellationToken.None);

        await assertOnlyBlueTreeIsLeftAsync();
    }

    [Fact]
    public async Task delete_all_tenant_data_with_a_self_referencing_managed_partitioned_document()
    {
        StoreOptions(configureManagedPartitionsWithSelfReference);

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "red", "blue");

        await storeTreeAsync("red");
        await storeTreeAsync("blue");

        await theStore.Advanced.DeleteAllTenantDataAsync("red", CancellationToken.None);

        await assertOnlyBlueTreeIsLeftAsync();
    }
}

public class TenantTreeNode
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
}
