using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Metadata;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.FSharp.Control;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Partitioning;

namespace MultiTenancyTests;

public class marten_managed_tenant_id_partitioning: OneOffConfigurationsContext, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try
        {
            await conn.DropSchemaAsync("tenants");

            // await conn.CreateCommand($"delete from tenants.{MartenManagedTenantListPartitions.TableName}")
            //     .ExecuteNonQueryAsync();
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
            .AddMartenManagedTenantsAsync(CancellationToken.None, "a1", "a2", "a3");

        #endregion

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var targetTable = await theStore.Storage.Database.ExistingTableFor(typeof(Target));
        assertTableHasTenantPartitions(targetTable, "a1", "a2", "a3");

        var userTable = await theStore.Storage.Database.ExistingTableFor(typeof(User));
        assertTableHasTenantPartitions(userTable, "a1", "a2", "a3");

    }

    [Fact]
    public async Task add_then_remove_tenants_at_runtime()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");

            opts.Schema.For<Target>();
            opts.Schema.For<User>();
        }, true);

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var statuses = await theStore
            .Advanced
            // This is ensuring that there are tenant id partitions for all multi-tenanted documents
            // with the named tenant ids
            .AddMartenManagedTenantsAsync(CancellationToken.None, "a1", "a2", "a3");

        foreach (var status in statuses)
        {
            status.Status.ShouldBe(PartitionMigrationStatus.Complete);
        }

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();

        await theStore.Advanced.RemoveMartenManagedTenantsAsync(["a2"], CancellationToken.None);

        var targetTable = await theStore.Storage.Database.ExistingTableFor(typeof(Target));
        assertTableHasTenantPartitions(targetTable, "a1", "a3");

        var userTable = await theStore.Storage.Database.ExistingTableFor(typeof(User));
        assertTableHasTenantPartitions(userTable, "a1", "a3");
    }

    [Fact]
    public async Task delete_all_tenant_data_will_drop_partitions()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;

            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");

            opts.Schema.For<Target>();
            opts.Schema.For<User>();
        }, true);

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        var statuses = await theStore
            .Advanced
            // This is ensuring that there are tenant id partitions for all multi-tenanted documents
            // with the named tenant ids
            .AddMartenManagedTenantsAsync(CancellationToken.None, "a1", "a2", "a3");

        foreach (var status in statuses)
        {
            status.Status.ShouldBe(PartitionMigrationStatus.Complete);
        }

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();

        await theStore.Advanced.DeleteAllTenantDataAsync("a2", CancellationToken.None);

        var targetTable = await theStore.Storage.Database.ExistingTableFor(typeof(Target));
        assertTableHasTenantPartitions(targetTable, "a1", "a3");

        var userTable = await theStore.Storage.Database.ExistingTableFor(typeof(User));
        assertTableHasTenantPartitions(userTable, "a1", "a3");
    }



    [Fact]
    public async Task should_not_build_storage_for_live_aggregations()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");

            opts.Events.TenancyStyle = TenancyStyle.Conjoined;

            opts.Schema.For<Target>();
            opts.Schema.For<User>();

            opts.Projections.LiveStreamAggregation<SimpleAggregate>();
        }, true);

        var streamId = theSession.Events.StartStream<SimpleAggregate>(new AEvent(), new BEvent()).Id;
        await theSession.SaveChangesAsync();

        var aggregate = theSession.Events.AggregateStreamAsync<SimpleAggregate>(streamId);
        aggregate.ShouldNotBeNull();

        await theStore
            .Advanced
            // This is ensuring that there are tenant id partitions for all multi-tenanted documents
            // with the named tenant ids
            .AddMartenManagedTenantsAsync(CancellationToken.None, "a1", "a2", "a3");

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Seeing if the table for SimpleAggregate exists, and it should *not*
        var table = await theStore.Storage.Database.ExistingTableFor(typeof(SimpleAggregate));
        table.ShouldBeNull();
    }

    [Fact]
    public async Task can_build_storage_with_dynamic_tenants_by_variable_tenant_and_suffix_mappings()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");

            opts.Schema.For<Target>();
            opts.Schema.For<User>();
        }, true);

        var tenantId1 = Guid.NewGuid().ToString();
        var tenantId2 = Guid.NewGuid().ToString();
        var tenantId3 = Guid.NewGuid().ToString();

        var names = new Dictionary<string, string>
        {
            { tenantId1, "a1" }, { tenantId2, "a2" }, { tenantId3, "a3" }
        };

        await theStore
            .Advanced
            // This is ensuring that there are tenant id partitions for all multi-tenanted documents
            // with the named tenant ids
            .AddMartenManagedTenantsAsync(CancellationToken.None, names);

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

        await theStore.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "a1", "a2", "a3");

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Little overlap to prove it's idempotent
        var statuses = await theStore.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "a1", "b1", "b2");
        foreach (var status in statuses)
        {
            status.Status.ShouldBe(PartitionMigrationStatus.Complete);
        }

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

    [Fact]
    public void exempt_from_partitioning_through_attribute_usage()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");

            opts.Schema.For<Target>();
            opts.Schema.For<User>();
        }, true);

        var mapping = theStore.Options.Storage.MappingFor(typeof(DocThatShouldBeExempted1));
        mapping.DisablePartitioningIfAny.ShouldBeTrue();

        var table = new DocumentTable(mapping);
        table.Partitioning.ShouldBeNull();
    }

    [Fact]
    public void exempt_from_partitioning_through_fluent_interface_usage()
    {
        StoreOptions(opts =>
        {
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement("tenants");

            opts.Schema.For<Target>();
            opts.Schema.For<User>();

            #region sample_exempt_from_partitioning_through_fluent_interface

            opts.Schema.For<DocThatShouldBeExempted2>().DoNotPartition();

            #endregion
        }, true);

        var mapping = theStore.Options.Storage.MappingFor(typeof(DocThatShouldBeExempted2));
        mapping.DisablePartitioningIfAny.ShouldBeTrue();

        var table = new DocumentTable(mapping);
        table.Partitioning.ShouldBeNull();
    }
}

#region sample_using_DoNotPartitionAttribute

[DoNotPartition]
public class DocThatShouldBeExempted1
{
    public Guid Id { get; set; }
}

#endregion

public class DocThatShouldBeExempted2
{
    public Guid Id { get; set; }
}

public class SimpleAggregate : IRevisioned
{
    // This will be the aggregate version
    public int Version { get; set; }

    public Guid Id { get; set; }

    public int ACount { get; set; }
    public int BCount { get; set; }
    public int CCount { get; set; }
    public int DCount { get; set; }
    public int ECount { get; set; }

    public void Apply(AEvent _)
    {
        ACount++;
    }

    public void Apply(BEvent _)
    {
        BCount++;
    }

    public void Apply(CEvent _)
    {
        CCount++;
    }

    public void Apply(DEvent _)
    {
        DCount++;
    }

    public void Apply(EEvent _)
    {
        ECount++;
    }
}

public class BEvent{}
public class CEvent{}
public class DEvent{}
public class EEvent{}


