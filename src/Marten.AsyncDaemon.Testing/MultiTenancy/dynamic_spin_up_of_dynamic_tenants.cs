using System.Threading.Tasks;
using JasperFx.Core;
using Marten.AsyncDaemon.Testing.TestingSupport;
using Marten.Events.Daemon.Coordination;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Xunit;
using Xunit.Abstractions;

namespace Marten.AsyncDaemon.Testing.MultiTenancy;

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

        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                    {
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
        var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;
        await tenancy.AddDatabaseRecordAsync("tenant1", tenant1ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant2", tenant2ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant3", tenant3ConnectionString);

        var coordinator = _host.Services.GetRequiredService<IProjectionCoordinator>();
        var daemon1 = await coordinator.DaemonForDatabase("tenant1");
        var daemon2 = await coordinator.DaemonForDatabase("tenant2");
        var daemon3 = await coordinator.DaemonForDatabase("tenant3");

        // I hate this.
        await Task.Delay(3.Seconds());

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
