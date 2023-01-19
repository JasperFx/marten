using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Services;
using Marten.Storage;
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
public class using_static_database_multitenancy: IAsyncLifetime
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

        return builder.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var db1ConnectionString = await CreateDatabaseIfNotExists(conn, "database1");
        var tenant3ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant3");
        var tenant4ConnectionString = await CreateDatabaseIfNotExists(conn, "tenant4");

        #region sample_using_multi_tenanted_databases

        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                    {
                        // Explicitly map tenant ids to database connection strings
                        opts.MultiTenantedDatabases(x =>
                        {
                            // Map multiple tenant ids to a single named database
                            x.AddMultipleTenantDatabase(db1ConnectionString,"database1").ForTenants("tenant1", "tenant2");

                            // Map a single tenant id to a database, which uses the tenant id as well for the database identifier
                            x.AddSingleTenantDatabase(tenant3ConnectionString, "tenant3");
                            x.AddSingleTenantDatabase(tenant4ConnectionString,"tenant4");
                        });


                        opts.RegisterDocumentType<User>();
                        opts.RegisterDocumentType<Target>();

                    })

                    // All detected changes will be applied to all
                    // the configured tenant databases on startup
                    .ApplyAllDatabaseChangesOnStartup();
            }).StartAsync();

        #endregion

        theStore = _host.Services.GetRequiredService<IDocumentStore>();
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
    public async Task creates_databases_from_apply()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        (await conn.DatabaseExists("database1")).ShouldBeTrue();
        (await conn.DatabaseExists("tenant3")).ShouldBeTrue();
        (await conn.DatabaseExists("tenant4")).ShouldBeTrue();
    }

    [Fact]
    public async Task changes_are_applied_to_each_database()
    {
        using var store = _host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();
        var databases = await store.Tenancy.BuildDatabases();

        foreach (IMartenDatabase database in databases)
        {
            await using var conn = database.CreateConnection();
            await conn.OpenAsync();

            var tables = await conn.ExistingTables();
            tables.Any(x => x.QualifiedName == "public.mt_doc_user").ShouldBeTrue();
            tables.Any(x => x.QualifiedName == "public.mt_doc_target").ShouldBeTrue();
        }
    }

    [Fact]
    public async Task can_open_a_session_to_a_different_database()
    {
        await using var session =
            await theStore.OpenSessionAsync(new SessionOptions
            {
                TenantId = "tenant1", Tracking = DocumentTracking.None
            });

        session.Connection.Database.ShouldBe("database1");
    }

    [Fact]
    public async Task can_use_bulk_inserts()
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

    [Fact]
    public async Task clean_crosses_the_tenanted_databases()
    {
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

    public static async Task administering_multiple_databases(IDocumentStore store)
    {
        #region sample_administering_multiple_databases

        // Apply all detected changes in every known database
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Only apply to the default database if not using multi-tenancy per
        // database
        await store.Storage.Database.ApplyAllConfiguredChangesToDatabaseAsync();

        // Find a specific database
        var database = await store.Storage.FindOrCreateDatabase("tenant1");

        // Tear down everything
        await database.CompletelyRemoveAllAsync();

        // Check out the projection state in just this database
        var state = await database.FetchEventStoreStatistics();

        // Apply all outstanding database changes in just this database
        await database.ApplyAllConfiguredChangesToDatabaseAsync();

        #endregion
    }
}
