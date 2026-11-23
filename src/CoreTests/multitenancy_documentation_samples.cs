using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoreTests.MultiTenancySamples;

// Compile-only samples for docs/configuration/multitenancy.md. Nothing here runs as a test.

public static class multitenancy_documentation_samples
{
    public static void provisioning_command_timeout_on_a_single_server(StoreOptions opts, string connectionString)
    {
        #region sample_provisioning_command_timeout_single_server

        opts.MultiTenantedWithSingleServer(connectionString, t =>
        {
            t.WithTenants("tenant1", "tenant2");
            t.ProvisioningCommandTimeout = TimeSpan.FromMinutes(5);
        });

        #endregion
    }

    public static void provisioning_command_timeout_for_created_databases(StoreOptions opts)
    {
        #region sample_provisioning_command_timeout_create_databases

        opts.CreateDatabasesForTenants(c =>
        {
            c.ProvisioningCommandTimeout(TimeSpan.FromMinutes(5));
            c.ForTenant().CheckAgainstPgDatabase();
        });

        opts.Advanced.Migrator.ProvisioningCommandTimeout = TimeSpan.FromMinutes(5);

        #endregion
    }

    public static void configure_sharded_tenancy(
        string masterConnectionString,
        string shard1ConnectionString,
        string shard2ConnectionString,
        string shard3ConnectionString,
        string shard4ConnectionString)
    {
        #region sample_configure_sharded_tenancy

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMarten(opts =>
        {
            opts.MultiTenantedWithShardedDatabases(x =>
            {
                // Connection to the master database that holds the pool registry
                x.ConnectionString = masterConnectionString;

                // Schema for the registry tables in the master database
                x.SchemaName = "tenants";

                // Schema for the partition tracking table within each tenant database
                x.PartitionSchemaName = "partitions";

                // Seed the database pool on startup
                x.AddDatabase("shard_01", shard1ConnectionString);
                x.AddDatabase("shard_02", shard2ConnectionString);
                x.AddDatabase("shard_03", shard3ConnectionString);
                x.AddDatabase("shard_04", shard4ConnectionString);

                // Choose a tenant assignment strategy (see below)
                x.UseHashAssignment(); // this is the default
            });
        });

        #endregion
    }

    public static async Task add_tenants_to_shards(IDocumentStore store, CancellationToken ct)
    {
        #region sample_sharded_tenancy_add_tenant_to_shard

        // Auto-assign a tenant using the configured strategy
        // Returns the database_id the tenant was assigned to
        var dbId = await store.Advanced.AddTenantToShardAsync("new-tenant", ct);

        // Explicitly assign a tenant to a specific database
        await store.Advanced.AddTenantToShardAsync("vip-tenant", "shard_01", ct);

        #endregion
    }

    public static async Task manage_the_database_pool(IDocumentStore store, string newConnectionString, CancellationToken ct)
    {
        #region sample_sharded_tenancy_manage_database_pool

        // Add a new database to the pool at runtime
        await store.Advanced.AddDatabaseToPoolAsync("shard_05", newConnectionString, ct);

        // Mark a database as full. No new tenants are assigned to it
        await store.Advanced.MarkDatabaseFullAsync("shard_01", ct);

        #endregion
    }

    public static async Task query_across_all_databases(IDocumentStore store)
    {
        #region sample_query_across_all_databases

        var users = await store.Advanced.QueryAcrossAllDatabasesAsync(
            async (session, ct) => await session.Query<User>()
                .Where(x => x.Age > 30)
                .ToListAsync(ct),
            maxParallelism: 16);

        // Or, when the query also needs the database itself (daemon progression, statistics, ...)
        var names = await store.Advanced.QueryAcrossAllDatabasesAsync<string>(
            (session, database, ct) => Task.FromResult<IReadOnlyList<string>>([database.Identifier]));

        #endregion
    }
}
