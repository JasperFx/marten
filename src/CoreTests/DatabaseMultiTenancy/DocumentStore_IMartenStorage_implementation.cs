using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using CoreTests.Diagnostics;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Xunit;

namespace CoreTests.DatabaseMultiTenancy;

[CollectionDefinition("multi-tenancy", DisableParallelization = true)]
public class DocumentStore_IMartenStorage_implementation : IAsyncLifetime
{
    private IHost _host;
    private IDocumentStore theStore;

    private async Task<string> CreateDatabaseIfNotExists(NpgsqlConnection conn, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

        var exists = await conn.DatabaseExists(databaseName);
        if (!exists)
        {
            await new DatabaseSpecification().BuildDatabase(conn, databaseName);
        }

        builder.Database = databaseName;

        var connectionString = builder.ConnectionString;

        await using var dbConn = new NpgsqlConnection(connectionString);
        await dbConn.OpenAsync();
        await dbConn.DropSchema("multi_tenancy");
        await dbConn.DropSchema("mt_events");

        return connectionString;
    }


    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var db1ConnectionString = await CreateDatabaseIfNotExists(conn, "database1");
        var tenant3ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant3");
        var tenant4ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant4");

        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.DatabaseSchemaName = "multi_tenancy";
                    opts.Events.DatabaseSchemaName = "mt_events";

                    opts.MultiTenantedDatabases(x =>
                    {
                        x.AddMultipleTenantDatabase(db1ConnectionString, "database1")
                            .ForTenants("tenant1", "tenant2");
                        x.AddSingleTenantDatabase(tenant3ConnectionString, "tenant3");
                        x.AddSingleTenantDatabase(tenant4ConnectionString, "tenant4");
                    });


                    opts.RegisterDocumentType<User>();
                    opts.RegisterDocumentType<Target>();

                    opts.Events.AddEventType(typeof(AEvent));

                });
            }).StartAsync();

        theStore = _host.Services.GetRequiredService<IDocumentStore>();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public void can_get_all_schemas()
    {
        theStore.Storage.AllSchemaNames().ShouldContain("multi_tenancy");
        theStore.Storage.AllSchemaNames().ShouldContain("mt_events");
    }

    [Fact]
    public void all_objects()
    {
        // Just a spot check
        var schemaObjects = theStore.Storage.AllObjects();
        schemaObjects
            .Any(x => x.Identifier.Name == "mt_doc_user")
            .ShouldBeTrue();
    }

    [Fact]
    public void to_database_script()
    {
        // just a spot check
        var script = theStore.Storage.ToDatabaseScript();
        script.ShouldNotBeNull();
    }

    [Fact]
    public async Task can_write_creation_script_to_file()
    {
        File.Delete("export.sql");
        await theStore.Storage.WriteCreationScriptToFile("export.sql".ToFullPath());
        File.Exists("export.sql").ShouldBeTrue();
    }

    [Fact]
    public Task write_scripts_by_type()
    {
        return theStore.Storage.WriteCreationScriptToFile("export".ToFullPath());
    }

    [Fact]
    public async Task can_find_the_database_by_identifier()
    {
        var database = await theStore.Storage.FindOrCreateDatabase("database1");
        database.Identifier.ShouldBe("database1");
    }

    [Fact]
    public async Task find_database_by_tenant_id()
    {
        var database = await theStore.Storage.FindOrCreateDatabase("tenant1");
        database.Identifier.ShouldBe("database1");
    }

    [Fact]
    public async Task apply_all_changes()
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        foreach (var database in await theStore.Storage.AllDatabases())
        {
            await database.AssertDatabaseMatchesConfigurationAsync();
        }
    }

    [Fact]
    public async Task all_databases_can_return()
    {
        var databases = await theStore.Storage.AllDatabases();
        databases.Count.ShouldBe(3);
    }
}
