using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Daemon.Coordination;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Xunit;

namespace DaemonTests.MultiTenancy;

public class dynamic_spin_up_of_dynamic_tenants : IAsyncLifetime
{
    private IHost _host;
    private IDocumentStore theStore;
    private string tenant1ConnectionString;
    private string tenant2ConnectionString;
    private string tenant3ConnectionString;
    private string tenant4ConnectionString;

    private async Task<string> CreateDatabaseIfNotExists(NpgsqlConnection conn, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        var exists = await conn.DatabaseExists(databaseName);
        if (!exists)
        {
            await new DatabaseSpecification().BuildDatabase(conn, databaseName);
        }

        builder.Database = databaseName;

        return builder.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await conn.DropSchemaAsync("tenants");


        tenant1ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant1");
        tenant2ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant2");
        tenant3ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant3");
        tenant4ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant4");

        // Setting up a Host with Multi-tenancy
        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                    {
                        // This is a new strategy for configuring tenant databases with Marten
                        // In this usage, Marten is tracking the tenant databases in a single table in the "master"
                        // database by tenant
                        opts.MultiTenantedDatabasesWithMasterDatabaseTable(ConnectionSource.ConnectionString, "tenants");

                        opts.RegisterDocumentType<User>();
                        opts.RegisterDocumentType<Target>();

                        opts.Projections.Add<TripProjectionWithCustomName>(ProjectionLifecycle.Async);
                    })
                    .AddAsyncDaemon(DaemonMode.Solo)

                    // All detected changes will be applied to all
                    // the configured tenant databases on startup
                    .ApplyAllDatabaseChangesOnStartup();

            }).StartAsync();

        theStore = _host.Services.GetRequiredService<IDocumentStore>();

        var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;
        await tenancy.ClearAllDatabaseRecordsAsync();
    }

    [Fact]
    public async Task add_tenant_database_and_verify_the_daemon_projections_are_running()
    {
        // In this code block, I'm adding new tenant databases to the system that I
        // would expect Marten to discover and start up an asynchronous projection
        // daemon for all three newly discovered databases
        var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;
        await tenancy.AddDatabaseRecordAsync("tenant1", tenant1ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant2", tenant2ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant3", tenant3ConnectionString);

        // This is a new service in Marten specifically to help you interrogate or
        // manipulate the state of running asynchronous projections within the current process
        var coordinator = _host.Services.GetRequiredService<IProjectionCoordinator>();
        var daemon1 = await coordinator.DaemonForDatabase("tenant1");
        var daemon2 = await coordinator.DaemonForDatabase("tenant2");
        var daemon3 = await coordinator.DaemonForDatabase("tenant3");

        // Just proving that the configured projections for the 3 new databases
        // are indeed spun up and running after Marten's new daemon coordinator
        // "finds" the new databases
        await daemon1.WaitForShardToBeRunning("TripCustomName:All", 30.Seconds());
        await daemon2.WaitForShardToBeRunning("TripCustomName:All", 30.Seconds());
        await daemon3.WaitForShardToBeRunning("TripCustomName:All", 30.Seconds());
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        theStore.Dispose();
    }
}
