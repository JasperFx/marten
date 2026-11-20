using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Descriptors;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Connections;

namespace MultiTenancyTests;

[Collection("multi-tenancy")]
public class SingleServerMultiTenancyTests: IAsyncLifetime
{
    // #6109: these names are whole databases on the shared test server, so they carry the 9.0 prefix
    private const string Tenant1 = ConnectionSource.DatabasePrefix + "tenant1";
    private const string Tenant2 = ConnectionSource.DatabasePrefix + "tenant2";
    private const string Tenant3 = ConnectionSource.DatabasePrefix + "tenant3";
    private const string Tenant4 = ConnectionSource.DatabasePrefix + "tenant4";
    private const string Tenant5 = ConnectionSource.DatabasePrefix + "tenant5";
    private const string Tenant6 = ConnectionSource.DatabasePrefix + "tenant6";
    private const string Database1 = ConnectionSource.DatabasePrefix + "database1";
    private const string Database2 = ConnectionSource.DatabasePrefix + "database2";

    private DefaultNpgsqlDataSourceFactory dataSourceFactory = new();
    private SingleServerMultiTenancy theTenancy;

    public async ValueTask InitializeAsync()
    {
        await DropDatabaseIfExists(Tenant1);
        await DropDatabaseIfExists(Tenant2);
        await DropDatabaseIfExists(Tenant3);
        await DropDatabaseIfExists(Database1);
        await DropDatabaseIfExists(Database2);

        theTenancy = new SingleServerMultiTenancy(dataSourceFactory, ConnectionSource.ConnectionString, new StoreOptions());
    }

    public ValueTask DisposeAsync()
    {
        return default;
    }

    private async Task DropDatabaseIfExists(string databaseName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.AdminConnectionString);
        await conn.OpenAsync();

        await conn.KillIdleSessions(databaseName);
        await conn.DropDatabase(databaseName);
    }

    private async Task<bool> DatabaseExists(string databaseName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        return await conn.DatabaseExists(databaseName);
    }

    [Fact]
    public void can_get_at_the_master_database()
    {
        theTenancy.As<ITenancyWithMasterDatabase>().TenantDatabase.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(Tenant1, Database1, true)]
    [InlineData(Tenant4, Database1, false)]
    public async Task tenant_is_in_database(string tenantId, string databaseName, bool isContained)
    {
        theTenancy.WithTenants(Tenant1, Tenant2, Tenant3)
            .InDatabaseNamed(Database1);

        theTenancy.WithTenants(Tenant4, Tenant5)
            .InDatabaseNamed(Database2);

        await theTenancy.BuildDatabases();

        var database = await theTenancy.FindOrCreateDatabase(databaseName);
        theTenancy.IsTenantStoredInCurrentDatabase(database, tenantId).ShouldBe(isContained);
    }

    [Fact]
    public async Task build_description()
    {
        theTenancy.WithTenants(Tenant1, Tenant2, Tenant3)
            .InDatabaseNamed(Database1);

        theTenancy.WithTenants(Tenant4, Tenant5)
            .InDatabaseNamed(Database2);

        theTenancy.WithTenants(Tenant6);

        await theTenancy.BuildDatabases();

        theTenancy.Cardinality.ShouldBe(DatabaseCardinality.DynamicMultiple);

        var description = await theTenancy.DescribeDatabasesAsync(CancellationToken.None);

        description.Cardinality.ShouldBe(DatabaseCardinality.DynamicMultiple);

        description.MainDatabase.ShouldBeNull();

        description.Databases.Select(x => x.DatabaseName).OrderBy(x => x)
            .ShouldBe([Database1, Database2, Tenant6]);

        description.Databases.Single(x => x.DatabaseName == Database1)
            .TenantIds.ShouldBe([Tenant1, Tenant2, Tenant3]);

        description.Databases.Single(x => x.DatabaseName == Database2)
            .TenantIds.ShouldBe([Tenant4, Tenant5]);

        description.Databases.Single(x => x.DatabaseName == Tenant6)
            .TenantIds.ShouldBe([Tenant6]);
    }

    [Fact]
    public void clean_should_be_a_composite()
    {
        theTenancy.Cleaner.ShouldBeOfType<CompositeDocumentCleaner>();
    }

    [Fact]
    public async Task build_database_on_the_fly()
    {
        (await DatabaseExists(Tenant1)).ShouldBeFalse();
        var database = await theTenancy.FindOrCreateDatabase(Tenant1);
        (await DatabaseExists(Tenant1)).ShouldBeTrue();

        var database2 = await theTenancy.FindOrCreateDatabase(Tenant1);

        database.ShouldBeSameAs(database);
    }

    [Fact]
    public async Task default_is_not_null()
    {
        (await DatabaseExists(Tenant2)).ShouldBeFalse();
        var tenant = await theTenancy.GetTenantAsync(Tenant2);
        (await DatabaseExists(Tenant2)).ShouldBeTrue();

        theTenancy.Default.ShouldNotBeNull();
    }

    [Fact]
    public async Task build_tenant_where_database_is_already_built()
    {
        (await DatabaseExists(Tenant1)).ShouldBeFalse();
        var database = await theTenancy.FindOrCreateDatabase(Tenant1);
        (await DatabaseExists(Tenant1)).ShouldBeTrue();
        var tenant = theTenancy.GetTenant(Tenant1);
        tenant.Database.ShouldBeSameAs(database);
        tenant.TenantId.ShouldBe(Tenant1);
    }

    [Fact]
    public async Task build_tenant_where_database_does_not_already_exist()
    {
        (await DatabaseExists(Tenant1)).ShouldBeFalse();
        var tenant = theTenancy.GetTenant(Tenant1);
        (await DatabaseExists(Tenant1)).ShouldBeTrue();
        var database = await theTenancy.FindOrCreateDatabase(Tenant1);
        tenant.Database.ShouldBeSameAs(database);
        tenant.TenantId.ShouldBe(Tenant1);
    }

    // #5985: the synchronous resolution honours the tenant-to-database mapping, and shares its database with the
    // asynchronous path
    [Fact]
    public async Task build_mapped_tenants_synchronously()
    {
        theTenancy.WithTenants(Tenant1, Tenant2).InDatabaseNamed(Database1);

        (await DatabaseExists(Database1)).ShouldBeFalse();
        var tenant1 = theTenancy.GetTenant(Tenant1);
        (await DatabaseExists(Database1)).ShouldBeTrue();
        (await DatabaseExists(Tenant1)).ShouldBeFalse();

        tenant1.TenantId.ShouldBe(Tenant1);
        tenant1.Database.Identifier.ShouldBe(Database1);

        theTenancy.GetTenant(Tenant2).Database.ShouldBeSameAs(tenant1.Database);
        (await theTenancy.GetTenantAsync(Tenant2)).Database.ShouldBeSameAs(tenant1.Database);
        (await theTenancy.FindOrCreateDatabase(Database1)).ShouldBeSameAs(tenant1.Database);
    }

    // #5985: Npgsql refuses synchronous I/O under multiplexing, so a multiplexed master connection keeps the
    // asynchronous path rather than failing with NotSupportedException
    [Fact]
    public async Task build_tenant_synchronously_when_the_master_connection_is_multiplexed()
    {
        var multiplexed = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            Multiplexing = true
        }.ConnectionString;

        var tenancy = new SingleServerMultiTenancy(dataSourceFactory, multiplexed, new StoreOptions());

        (await DatabaseExists(Tenant3)).ShouldBeFalse();
        var tenant = tenancy.GetTenant(Tenant3);
        (await DatabaseExists(Tenant3)).ShouldBeTrue();

        tenant.Database.ShouldBeSameAs(await tenancy.FindOrCreateDatabase(Tenant3));
        await tenancy.DisposeAsync();
    }

    [Fact]
    public async Task build_tenant_where_database_is_already_built_async()
    {
        (await DatabaseExists(Tenant1)).ShouldBeFalse();
        var database = await theTenancy.FindOrCreateDatabase(Tenant1);
        (await DatabaseExists(Tenant1)).ShouldBeTrue();
        var tenant = await theTenancy.GetTenantAsync(Tenant1);
        tenant.Database.ShouldBeSameAs(database);
        tenant.TenantId.ShouldBe(Tenant1);
    }

    [Fact]
    public async Task build_tenant_where_database_does_not_already_exist_async()
    {
        (await DatabaseExists(Tenant1)).ShouldBeFalse();
        var tenant = await theTenancy.GetTenantAsync(Tenant1);
        (await DatabaseExists(Tenant1)).ShouldBeTrue();
        var database = await theTenancy.FindOrCreateDatabase(Tenant1);
        tenant.Database.ShouldBeSameAs(database);
        tenant.TenantId.ShouldBe(Tenant1);
    }


    [Fact]
    public async Task seed_tenant_id_and_build_in_advance()
    {
        theTenancy.WithTenants(Tenant1, Tenant2, Tenant3);

        var databases = await theTenancy.BuildDatabases();
        databases = databases.OrderBy(x => x.Identifier).ToList();

        databases.Select(x => x.Identifier).ShouldHaveTheSameElementsAs(Tenant1, Tenant2, Tenant3);

        databases[0].ShouldBeSameAs(await theTenancy.FindOrCreateDatabase(Tenant1));
        databases[1].ShouldBeSameAs(await theTenancy.FindOrCreateDatabase(Tenant2));
        databases[2].ShouldBeSameAs(await theTenancy.FindOrCreateDatabase(Tenant3));
    }

    [Fact]
    public async Task seed_tenant_id_and_build_in_advance_with_database_mappings()
    {
        theTenancy.WithTenants(Tenant1, Tenant2, Tenant3)
            .InDatabaseNamed(Database1);

        theTenancy.WithTenants(Tenant4, Tenant5)
            .InDatabaseNamed(Database2);

        var databases = await theTenancy.BuildDatabases();
        databases = databases.OrderBy(x => x.Identifier).ToList();

        databases.Select(x => x.Identifier).ShouldHaveTheSameElementsAs(Database1, Database2);

        databases[0].ShouldBeSameAs(await theTenancy.FindOrCreateDatabase(Database1));
        databases[1].ShouldBeSameAs(await theTenancy.FindOrCreateDatabase(Database2));
    }

    [Fact]
    public async Task map_tenants_to_databases()
    {
        theTenancy.WithTenants(Tenant1, Tenant2, Tenant3)
            .InDatabaseNamed(Database1);

        theTenancy.WithTenants(Tenant4, Tenant5)
            .InDatabaseNamed(Database2);

        await theTenancy.BuildDatabases();

        theTenancy.GetTenant(Tenant1).Database.Identifier.ShouldBe(Database1);
        theTenancy.GetTenant(Tenant2).Database.Identifier.ShouldBe(Database1);
        theTenancy.GetTenant(Tenant3).Database.Identifier.ShouldBe(Database1);
        theTenancy.GetTenant(Tenant4).Database.Identifier.ShouldBe(Database2);
        theTenancy.GetTenant(Tenant5).Database.Identifier.ShouldBe(Database2);
    }
}
