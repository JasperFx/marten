using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Descriptors;
using Marten;
using Marten.Exceptions;
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

namespace MultiTenancyTests;

[Collection("multi-tenancy")]
public class using_per_database_multitenancy: IAsyncLifetime
{
    // #6109: these names are whole databases on the shared test server, so they carry the 9.0 prefix
    private const string Tenant1 = ConnectionSource.DatabasePrefix + "tenant1";
    private const string Tenant2 = ConnectionSource.DatabasePrefix + "tenant2";
    private const string Tenant3 = ConnectionSource.DatabasePrefix + "tenant3";
    private const string Tenant4 = ConnectionSource.DatabasePrefix + "tenant4";
    private const string Database1 = ConnectionSource.DatabasePrefix + "database1";

    private IHost _host;
    private IDocumentStore theStore;

    #region sample_myspecialtenancy

    // Make sure you implement the Dispose() method and
    // dispose all MartenDatabase objects
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

        public bool IsTenantStoredInCurrentDatabase(IMartenDatabase database, string tenantId)
        {
            throw new System.NotImplementedException();
        }

        public void Dispose()
        {
            //
        }

        public async ValueTask<DatabaseUsage> DescribeDatabasesAsync(CancellationToken token)
        {
            throw new System.NotImplementedException();
        }

        public DatabaseCardinality Cardinality { get; }
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

    public async ValueTask InitializeAsync()
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
                        .MultiTenantedWithSingleServer(
                            ConnectionSource.ConnectionString,
                            t => t
                                // You can map multiple tenant ids to a single named database
                                .WithTenants(Tenant1, Tenant2).InDatabaseNamed(Database1)

                                // Just declaring that there are additional tenant ids that should
                                // have their own database
                                .WithTenants(Tenant3, Tenant4) // own database
                        );


                    opts.RegisterDocumentType<User>();
                    opts.RegisterDocumentType<Target>();
                }).ApplyAllDatabaseChangesOnStartup();
            }).StartAsync();

        #endregion

        theStore = _host.Services.GetRequiredService<IDocumentStore>();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        theStore.Dispose();
    }

    [Theory]
    [InlineData(Tenant3, Tenant3, true)]
    [InlineData(Tenant1, Database1, true)]
    [InlineData(Tenant3, Tenant4, false)]
    [InlineData(Tenant3, Database1, false)]
    public async Task tenant_is_in_database(string tenantId, string databaseName, bool isContained)
    {
        var tenancy = theStore.Options.Tenancy;
        var database = await tenancy.FindOrCreateDatabase(databaseName);
        tenancy.IsTenantStoredInCurrentDatabase(database, tenantId).ShouldBe(isContained);
    }

    [Fact]
    public async Task assert_invalid_tenant_for_database()
    {
        using var session = theStore.LightweightSession(Tenant1);
        Should.Throw<InvalidTenantForDatabaseException>(() =>
        {
            var nested = session.ForTenant(Tenant4);
        });
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
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        (await conn.DatabaseExists(Database1, TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await conn.DatabaseExists(Tenant3, TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await conn.DatabaseExists(Tenant4, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task changes_are_applied_to_each_database()
    {
        using var store = _host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();
        var databases = await store.Tenancy.BuildDatabases();

        foreach (IMartenDatabase database in databases)
        {
            await using var conn = database.CreateConnection();
            await conn.OpenAsync(TestContext.Current.CancellationToken);

            var tables = await conn.ExistingTablesAsync(ct: TestContext.Current.CancellationToken);
            tables.Any(x => x.QualifiedName == "public.mt_doc_user").ShouldBeTrue();
            tables.Any(x => x.QualifiedName == "public.mt_doc_target").ShouldBeTrue();
        }
    }

    [Fact]
    public async Task can_open_a_session_to_a_different_database()
    {
        await using var session =
            await theStore.LightweightSerializableSessionAsync(new SessionOptions { TenantId = Tenant1 }, TestContext.Current.CancellationToken);

        session.Connection.Database.ShouldBe(Database1);
    }

    [Fact]
    public async Task can_use_bulk_inserts()
    {
        var targets3 = Target.GenerateRandomData(100).ToArray();
        var targets4 = Target.GenerateRandomData(50).ToArray();

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync(TestContext.Current.CancellationToken);

        await theStore.BulkInsertDocumentsAsync(Tenant3, targets3, cancellation: TestContext.Current.CancellationToken);
        await theStore.BulkInsertDocumentsAsync(Tenant4, targets4, cancellation: TestContext.Current.CancellationToken);

        await using (var query3 = theStore.QuerySession(Tenant3))
        {
            var ids = await query3.Query<Target>().Select(x => x.Id).ToListAsync(TestContext.Current.CancellationToken);

            ids.OrderBy(x => x).ShouldHaveTheSameElementsAs(targets3.OrderBy(x => x.Id).Select(x => x.Id).ToList());
        }

        await using (var query4 = theStore.QuerySession(Tenant4))
        {
            var ids = await query4.Query<Target>().Select(x => x.Id).ToListAsync(TestContext.Current.CancellationToken);

            ids.OrderBy(x => x).ShouldHaveTheSameElementsAs(targets4.OrderBy(x => x.Id).Select(x => x.Id).ToList());
        }
    }

    [Fact]
    public async Task clean_crosses_the_tenanted_databases()
    {
        var targets3 = Target.GenerateRandomData(100).ToArray();
        var targets4 = Target.GenerateRandomData(50).ToArray();

        await theStore.BulkInsertDocumentsAsync(Tenant3, targets3, cancellation: TestContext.Current.CancellationToken);
        await theStore.BulkInsertDocumentsAsync(Tenant4, targets4, cancellation: TestContext.Current.CancellationToken);

        await theStore.Advanced.Clean.DeleteAllDocumentsAsync(TestContext.Current.CancellationToken);

        await using (var query3 = theStore.QuerySession(Tenant3))
        {
            (await query3.Query<Target>().AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        }

        await using (var query4 = theStore.QuerySession(Tenant4))
        {
            (await query4.Query<Target>().AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        }
    }
}
