using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Partitioning;

namespace MultiTenancyTests;

public class marten_managed_tenant_id_partitioning : OneOffConfigurationsContext, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try
        {
            await conn.CreateCommand($"delete from tenants.{MartenManagedTenantListPartitions.TableName}")
                .ExecuteNonQueryAsync();
        }
        catch (Exception)
        {
            // being lazy here
        }
        await conn.CloseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task can_build_storage_with_dynamic_tenants()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");

            opts.Schema.For<Target>();
            opts.Schema.For<User>();
        }, true);

        #region sample_add_managed_tenants_at_runtime

        await theStore
            .Advanced
            // This is ensuring that there are tenant id partitions for all multi-tenanted documents
            // with the named tenant ids
            .AddMartenManagedTenantsAsync(CancellationToken.None,"a1", "a2", "a3");

            #endregion

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var targetTable = await theStore.Storage.Database.ExistingTableFor(typeof(Target));
        assertTableHasTenantPartitions(targetTable, "a1", "a2", "a3");

        var userTable = await theStore.Storage.Database.ExistingTableFor(typeof(User));
        assertTableHasTenantPartitions(userTable, "a1", "a2", "a3");

    }

    [Fact]
    public async Task can_build_then_add_additive_partitions_later()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");

            opts.Schema.For<Target>();
            opts.Schema.For<User>();
        }, true);

        await theStore.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None,"a1", "a2", "a3");

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Little overlap to prove it's idempotent
        await theStore.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None,"a1", "b1", "b2");

        var targetTable = await theStore.Storage.Database.ExistingTableFor(typeof(Target));
        assertTableHasTenantPartitions(targetTable, "a1", "a2", "a3", "b1", "b2");

        var userTable = await theStore.Storage.Database.ExistingTableFor(typeof(User));
        assertTableHasTenantPartitions(userTable, "a1", "a2", "a3", "b1", "b2");
    }

    private void assertTableHasTenantPartitions(Table table, params string[] tenantIds)
    {
        var partitioning = table.Partitioning.ShouldBeOfType<ListPartitioning>();
        partitioning.Partitions.Select(x => x.Suffix).OrderBy(x => x)
            .ShouldBe(tenantIds);
    }

    public static async Task sample_configuration()
    {
#if NET8_0_OR_GREATER

        #region sample_configure_marten_managed_tenant_partitioning

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.Connection(builder.Configuration.GetConnectionString("marten"));

            // Make all document types use "conjoined" multi-tenancy -- unless explicitly marked with
            // [SingleTenanted] or explicitly configured via the fluent interfce
            // to be single-tenanted
            opts.Policies.AllDocumentsAreMultiTenanted();

            // It's required to explicitly tell Marten which database schema to put
            // the mt_tenant_partitions table
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");
        });

#endregion
#endif

    }
}


