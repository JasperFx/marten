using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;

namespace MultiTenancyTests;

public class master_table_multi_tenancy_independent_auto_create
{
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

    [Fact]
    public async Task can_still_create_own_tables()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await conn.DropSchemaAsync("tenants");

        var tenant1ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant1");
        var tenant2ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant2");
        var tenant3ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant3");
        var tenant4ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant4");

        await conn.CloseAsync();

        using var host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                    {
                        opts.AutoCreateSchemaObjects = AutoCreate.None;

                        opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
                        {
                            x.ConnectionString = ConnectionSource.ConnectionString;
                            x.SchemaName = "tenants";
                            x.ApplicationName = "Sample";

                            x.AutoCreate = AutoCreate.CreateOrUpdate;

                            x.RegisterDatabase("tenant1", tenant1ConnectionString);
                            x.RegisterDatabase("tenant2", tenant2ConnectionString);
                            x.RegisterDatabase("tenant3", tenant3ConnectionString);
                        });

                        opts.RegisterDocumentType<User>();
                        opts.RegisterDocumentType<Target>();
                    })

                    // All detected changes will be applied to all
                    // the configured tenant databases on startup
                    .ApplyAllDatabaseChangesOnStartup();
            }).StartAsync();

        await host.AddTenantDatabaseAsync("tenant4", tenant4ConnectionString);
    }
}


[CollectionDefinition("multi-tenancy", DisableParallelization = true)]
public class master_table_multi_tenancy_seeding : IAsyncLifetime
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
                        // This connection string is

                        opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
                        {
                            x.ConnectionString = ConnectionSource.ConnectionString;
                            x.SchemaName = "tenants";
                            x.ApplicationName = "Sample";
                            x.RegisterDatabase("tenant1", tenant1ConnectionString);
                            x.RegisterDatabase("tenant2", tenant2ConnectionString);
                            x.RegisterDatabase("tenant3", tenant3ConnectionString);
                            x.RegisterDatabase("tenant4", tenant4ConnectionString);
                        });

                        opts.RegisterDocumentType<User>();
                        opts.RegisterDocumentType<Target>();
                    })

                    // All detected changes will be applied to all
                    // the configured tenant databases on startup
                    .ApplyAllDatabaseChangesOnStartup();
            }).StartAsync();


        theStore = _host.Services.GetRequiredService<IDocumentStore>();

        await _host.ClearAllTenantDatabaseRecordsAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        theStore.Dispose();
    }

    [Fact]
    public void add_application_name_to_connections()
    {
        using var query3 = theStore.QuerySession("tenant3");
        var builder = new NpgsqlConnectionStringBuilder(query3.Connection.ConnectionString);
        builder.ApplicationName.ShouldBe("Sample");
    }

    [Fact]
    public async Task can_be_immediately_using_tenant_databases_that_were_seeded()
    {
        var targets3 = Target.GenerateRandomData(100).ToArray();
        var targets4 = Target.GenerateRandomData(50).ToArray();

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();

        await theStore.BulkInsertDocumentsAsync("tenant3", targets3);
        await theStore.BulkInsertDocumentsAsync("tenant4", targets4);

        await using (var query3 = theStore.QuerySession("tenant3"))
        {
            var ids = await query3.Query<Target>().Select(x => x.Id).ToListAsync();

            ids.OrderBy(x => x).ShouldHaveTheSameElementsAs(targets3.OrderBy(x => x.Id).Select(x => x.Id).ToList());
        }

        await using (var query4 = theStore.QuerySession("tenant4"))
        {
            var ids = await query4.Query<Target>().Select(x => x.Id).ToListAsync();

            ids.OrderBy(x => x).ShouldHaveTheSameElementsAs(targets4.OrderBy(x => x.Id).Select(x => x.Id).ToList());
        }
    }

}

[CollectionDefinition("multi-tenancy", DisableParallelization = true)]
public class using_master_table_multi_tenancy : IAsyncLifetime
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
                        // This connection string is

                        opts.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
                        {
                            x.ConnectionString = ConnectionSource.ConnectionString;
                            x.SchemaName = "tenants";
                            x.ApplicationName = "Sample";

                        });

                        opts.RegisterDocumentType<User>();
                        opts.RegisterDocumentType<Target>();
                    })

                    // All detected changes will be applied to all
                    // the configured tenant databases on startup
                    .ApplyAllDatabaseChangesOnStartup();
            }).StartAsync();


        theStore = _host.Services.GetRequiredService<IDocumentStore>();

        await _host.ClearAllTenantDatabaseRecordsAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        theStore.Dispose();
    }

    [Fact]
    public void default_tenant_usage_is_disabled()
    {
        theStore.Options.Advanced
            .DefaultTenantUsageEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task can_open_a_session_to_a_different_database()
    {
        await _host.AddTenantDatabaseAsync("tenant1", tenant1ConnectionString);
        await _host.AddTenantDatabaseAsync("tenant2", tenant2ConnectionString);
        await _host.AddTenantDatabaseAsync("tenant3", tenant3ConnectionString);
        await _host.AddTenantDatabaseAsync("tenant4", tenant4ConnectionString);

        await using var session =
            await theStore.LightweightSerializableSessionAsync(new SessionOptions { TenantId = "tenant1" });

        session.Connection.Database.ShouldBe("tenant1");
    }

    [Fact]
    public async Task can_use_bulk_inserts()
    {
        var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;

        await tenancy.AddDatabaseRecordAsync("tenant1", tenant1ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant2", tenant2ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant3", tenant3ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant4", tenant4ConnectionString);

        var targets3 = Target.GenerateRandomData(100).ToArray();
        var targets4 = Target.GenerateRandomData(50).ToArray();

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();

        await theStore.BulkInsertDocumentsAsync("tenant3", targets3);
        await theStore.BulkInsertDocumentsAsync("tenant4", targets4);

        await using (var query3 = theStore.QuerySession("tenant3"))
        {
            var ids = await query3.Query<Target>().Select(x => x.Id).ToListAsync();

            ids.OrderBy(x => x).ShouldHaveTheSameElementsAs(targets3.OrderBy(x => x.Id).Select(x => x.Id).ToList());
        }

        await using (var query4 = theStore.QuerySession("tenant4"))
        {
            var ids = await query4.Query<Target>().Select(x => x.Id).ToListAsync();

            ids.OrderBy(x => x).ShouldHaveTheSameElementsAs(targets4.OrderBy(x => x.Id).Select(x => x.Id).ToList());
        }
    }

    [Fact]
    public async Task clean_crosses_the_tenanted_databases()
    {
        var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;

        await tenancy.AddDatabaseRecordAsync("tenant1", tenant1ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant2", tenant2ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant3", tenant3ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant4", tenant4ConnectionString);

        var targets3 = Target.GenerateRandomData(100).ToArray();
        var targets4 = Target.GenerateRandomData(50).ToArray();

        await theStore.BulkInsertDocumentsAsync("tenant3", targets3);
        await theStore.BulkInsertDocumentsAsync("tenant4", targets4);

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();

        await using (var query3 = theStore.QuerySession("tenant3"))
        {
            (await query3.Query<Target>().AnyAsync()).ShouldBeFalse();
        }

        await using (var query4 = theStore.QuerySession("tenant4"))
        {
            (await query4.Query<Target>().AnyAsync()).ShouldBeFalse();
        }
    }

    [Fact]
    public void can_start_up_without_error()
    {
        true.ShouldBe(true);
    }

    [Fact]
    public async Task smoke_test_add_tenants()
    {
        var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;

        await tenancy.AddDatabaseRecordAsync("tenant1", tenant1ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant2", tenant2ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant3", tenant3ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant4", tenant4ConnectionString);

        var databases = await tenancy.BuildDatabases();

        databases.Count.ShouldBe(5);
        databases.OfType<MasterTableTenancy.TenantDatabase>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task delete_a_single_database()
    {
        var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;

        await tenancy.AddDatabaseRecordAsync("tenant1", tenant1ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant2", tenant2ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant3", tenant3ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant4", tenant4ConnectionString);

        await tenancy.DeleteDatabaseRecordAsync("tenant3");

        var databases = await tenancy.BuildDatabases();
        databases.OfType<MartenDatabase>().Select(x => x.Identifier).OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("tenant1", "tenant2", "tenant4");
    }

    [Fact]
    public async Task clean_all_database_records()
    {
        var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;

        await tenancy.AddDatabaseRecordAsync("tenant1", tenant1ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant2", tenant2ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant3", tenant3ConnectionString);
        await tenancy.AddDatabaseRecordAsync("tenant4", tenant4ConnectionString);

        await tenancy.ClearAllDatabaseRecordsAsync();

        var databases = await tenancy.BuildDatabases();

        databases.OfType<MartenDatabase>().Any().ShouldBeFalse();
    }

    [Fact]
    public async Task get_tenant_hit()
    {
        var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;

        await tenancy.AddDatabaseRecordAsync("tenant1", tenant1ConnectionString);
        await tenancy.BuildDatabases();

        var tenant = tenancy.GetTenant("tenant1");
        tenant.Database.CreateConnection().Database.ShouldBe("tenant1");
    }

    [Fact]
    public async Task get_tenant_async_hit()
    {
        var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;

        await tenancy.AddDatabaseRecordAsync("tenant1", tenant1ConnectionString);
        await tenancy.BuildDatabases();

        var tenant = await tenancy.GetTenantAsync("tenant1");
        tenant.Database.CreateConnection().Database.ShouldBe("tenant1");
    }

    [Fact]
    public async Task get_tenant_miss()
    {
        var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;

        await tenancy.AddDatabaseRecordAsync("tenant1", tenant1ConnectionString);
        await tenancy.BuildDatabases();

        Should.Throw<UnknownTenantIdException>(() =>
        {
            var tenant = tenancy.GetTenant("wrong");
        });
    }

    [Fact]
    public async Task get_tenant_async_miss()
    {
        var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;

        await tenancy.AddDatabaseRecordAsync("tenant1", tenant1ConnectionString);
        await tenancy.BuildDatabases();

        await Should.ThrowAsync<UnknownTenantIdException>(async () =>
        {
            await tenancy.GetTenantAsync("wrong");
        });
    }

    [Fact]
    public async Task get_tenant_miss_dynamic_hit()
    {
        var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;

        await tenancy.AddDatabaseRecordAsync("tenant1", tenant1ConnectionString);
        await tenancy.BuildDatabases();

        await tenancy.AddDatabaseRecordAsync("tenant2", tenant2ConnectionString);

        var tenant = tenancy.GetTenant("tenant2");
        tenant.Database.CreateConnection().Database.ShouldBe("tenant2");

    }

    [Fact]
    public async Task get_tenant_async_dynamic_hit()
    {
        var tenancy = (MasterTableTenancy)theStore.Options.Tenancy;

        await tenancy.AddDatabaseRecordAsync("tenant1", tenant1ConnectionString);
        await tenancy.BuildDatabases();

        await tenancy.AddDatabaseRecordAsync("tenant2", tenant2ConnectionString);

        var tenant = await tenancy.GetTenantAsync("tenant2");

        tenant.Database.CreateConnection().Database.ShouldBe("tenant2");

    }
}
