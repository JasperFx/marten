using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests.DatabaseMultiTenancy;

[CollectionDefinition("multi-tenancy", DisableParallelization = true)]
public class using_per_database_multitenancy : IAsyncLifetime
{
    private IHost _host;
    private IDocumentStore theStore;

    #region sample_MySpecialTenancy

    public class MySpecialTenancy: ITenancy

        #endregion
    {
        public ValueTask<IReadOnlyList<IDatabase>> BuildDatabases()
        {
            throw new System.NotImplementedException();
        }

        public Tenant GetTenant(string tenantId)
        {
            throw new System.NotImplementedException();
        }

        public Tenant Default { get; }
        public IDocumentCleaner Cleaner { get; }
        public ValueTask<Tenant> GetTenantAsync(string tenantId)
        {
            throw new System.NotImplementedException();
        }

        public ValueTask<IMartenDatabase> FindOrCreateDatabase(string tenantIdOrDatabaseIdentifier)
        {
            throw new System.NotImplementedException();
        }
    }

    public static void apply_custom_tenancy()
    {
        #region sample_apply_custom_tenancy

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("connection string");

            // Apply custom tenancy model
            opts.Tenancy = new MySpecialTenancy();
        });

        #endregion
    }

    public async Task InitializeAsync()
    {
        #region sample_using_single_server_multi_tenancy

        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts
                        // You have to specify a connection string for "administration"
                        // with rights to provision new databases on the fly
                        .MultiTenantedWithSingleServer(ConnectionSource.ConnectionString)

                        // You can map multiple tenant ids to a single named database
                        .WithTenants("tenant1", "tenant2").InDatabaseNamed("database1")

                        // Just declaring that there are additional tenant ids that should
                        // have their own database
                        .WithTenants("tenant3", "tenant4"); // own database


                    opts.RegisterDocumentType<User>();
                    opts.RegisterDocumentType<Target>();

                }).ApplyAllDatabaseChangesOnStartup();
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
}
