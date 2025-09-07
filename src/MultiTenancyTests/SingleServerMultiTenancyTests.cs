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

[CollectionDefinition("multi-tenancy", DisableParallelization = true)]
public class SingleServerMultiTenancyTests: IAsyncLifetime
{
    private DefaultNpgsqlDataSourceFactory dataSourceFactory = new();
    private SingleServerMultiTenancy theTenancy;

    public async Task InitializeAsync()
    {
        await DropDatabaseIfExists("tenant1");
        await DropDatabaseIfExists("tenant2");
        await DropDatabaseIfExists("tenant3");
        await DropDatabaseIfExists("database1");
        await DropDatabaseIfExists("database2");

        theTenancy = new SingleServerMultiTenancy(dataSourceFactory, ConnectionSource.ConnectionString, new StoreOptions());
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    private async Task DropDatabaseIfExists(string databaseName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
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
    [InlineData("tenant1", "database1", true)]
    [InlineData("tenant4", "database1", false)]
    public async Task tenant_is_in_database(string tenantId, string databaseName, bool isContained)
    {
        theTenancy.WithTenants("tenant1", "tenant2", "tenant3")
            .InDatabaseNamed("database1");

        theTenancy.WithTenants("tenant4", "tenant5")
            .InDatabaseNamed("database2");

        await theTenancy.BuildDatabases();

        var database = await theTenancy.FindOrCreateDatabase(databaseName);
        theTenancy.IsTenantStoredInCurrentDatabase(database, tenantId).ShouldBe(isContained);
    }

    [Fact]
    public async Task build_description()
    {
        theTenancy.WithTenants("tenant1", "tenant2", "tenant3")
            .InDatabaseNamed("database1");

        theTenancy.WithTenants("tenant4", "tenant5")
            .InDatabaseNamed("database2");

        theTenancy.WithTenants("tenant6");

        await theTenancy.BuildDatabases();

        theTenancy.Cardinality.ShouldBe(DatabaseCardinality.DynamicMultiple);

        var description = await theTenancy.DescribeDatabasesAsync(CancellationToken.None);

        description.Cardinality.ShouldBe(DatabaseCardinality.DynamicMultiple);

        description.MainDatabase.ShouldBeNull();

        description.Databases.Select(x => x.DatabaseName).OrderBy(x => x)
            .ShouldBe(["database1", "database2", "tenant6"]);

        description.Databases.Single(x => x.DatabaseName == "database1")
            .TenantIds.ShouldBe(["tenant1", "tenant2", "tenant3"]);

        description.Databases.Single(x => x.DatabaseName == "database2")
            .TenantIds.ShouldBe(["tenant4", "tenant5"]);

        description.Databases.Single(x => x.DatabaseName == "tenant6")
            .TenantIds.ShouldBe(["tenant6"]);
    }

    [Fact]
    public void clean_should_be_a_composite()
    {
        theTenancy.Cleaner.ShouldBeOfType<CompositeDocumentCleaner>();
    }

    [Fact]
    public async Task build_database_on_the_fly()
    {
        (await DatabaseExists("tenant1")).ShouldBeFalse();
        var database = await theTenancy.FindOrCreateDatabase("tenant1");
        (await DatabaseExists("tenant1")).ShouldBeTrue();

        var database2 = await theTenancy.FindOrCreateDatabase("tenant1");

        database.ShouldBeSameAs(database);
    }

    [Fact]
    public async Task default_is_not_null()
    {
        (await DatabaseExists("tenant2")).ShouldBeFalse();
        var tenant = await theTenancy.GetTenantAsync("tenant2");
        (await DatabaseExists("tenant2")).ShouldBeTrue();

        theTenancy.Default.ShouldNotBeNull();
    }

    [Fact]
    public async Task build_tenant_where_database_is_already_built()
    {
        (await DatabaseExists("tenant1")).ShouldBeFalse();
        var database = await theTenancy.FindOrCreateDatabase("tenant1");
        (await DatabaseExists("tenant1")).ShouldBeTrue();
        var tenant = theTenancy.GetTenant("tenant1");
        tenant.Database.ShouldBeSameAs(database);
        tenant.TenantId.ShouldBe("tenant1");
    }

    [Fact]
    public async Task build_tenant_where_database_does_not_already_exist()
    {
        (await DatabaseExists("tenant1")).ShouldBeFalse();
        var tenant = theTenancy.GetTenant("tenant1");
        (await DatabaseExists("tenant1")).ShouldBeTrue();
        var database = await theTenancy.FindOrCreateDatabase("tenant1");
        tenant.Database.ShouldBeSameAs(database);
        tenant.TenantId.ShouldBe("tenant1");
    }

    [Fact]
    public async Task build_tenant_where_database_is_already_built_async()
    {
        (await DatabaseExists("tenant1")).ShouldBeFalse();
        var database = await theTenancy.FindOrCreateDatabase("tenant1");
        (await DatabaseExists("tenant1")).ShouldBeTrue();
        var tenant = await theTenancy.GetTenantAsync("tenant1");
        tenant.Database.ShouldBeSameAs(database);
        tenant.TenantId.ShouldBe("tenant1");
    }

    [Fact]
    public async Task build_tenant_where_database_does_not_already_exist_async()
    {
        (await DatabaseExists("tenant1")).ShouldBeFalse();
        var tenant = await theTenancy.GetTenantAsync("tenant1");
        (await DatabaseExists("tenant1")).ShouldBeTrue();
        var database = await theTenancy.FindOrCreateDatabase("tenant1");
        tenant.Database.ShouldBeSameAs(database);
        tenant.TenantId.ShouldBe("tenant1");
    }


    [Fact]
    public async Task seed_tenant_id_and_build_in_advance()
    {
        theTenancy.WithTenants("tenant1", "tenant2", "tenant3");

        var databases = await theTenancy.BuildDatabases();
        databases = databases.OrderBy(x => x.Identifier).ToList();

        databases.Select(x => x.Identifier).ShouldHaveTheSameElementsAs("tenant1", "tenant2", "tenant3");

        databases[0].ShouldBeSameAs(await theTenancy.FindOrCreateDatabase("tenant1"));
        databases[1].ShouldBeSameAs(await theTenancy.FindOrCreateDatabase("tenant2"));
        databases[2].ShouldBeSameAs(await theTenancy.FindOrCreateDatabase("tenant3"));
    }

    [Fact]
    public async Task seed_tenant_id_and_build_in_advance_with_database_mappings()
    {
        theTenancy.WithTenants("tenant1", "tenant2", "tenant3")
            .InDatabaseNamed("database1");

        theTenancy.WithTenants("tenant4", "tenant5")
            .InDatabaseNamed("database2");

        var databases = await theTenancy.BuildDatabases();
        databases = databases.OrderBy(x => x.Identifier).ToList();

        databases.Select(x => x.Identifier).ShouldHaveTheSameElementsAs("database1", "database2");

        databases[0].ShouldBeSameAs(await theTenancy.FindOrCreateDatabase("database1"));
        databases[1].ShouldBeSameAs(await theTenancy.FindOrCreateDatabase("database2"));
    }

    [Fact]
    public async Task map_tenants_to_databases()
    {
        theTenancy.WithTenants("tenant1", "tenant2", "tenant3")
            .InDatabaseNamed("database1");

        theTenancy.WithTenants("tenant4", "tenant5")
            .InDatabaseNamed("database2");

        await theTenancy.BuildDatabases();

        theTenancy.GetTenant("tenant1").Database.Identifier.ShouldBe("database1");
        theTenancy.GetTenant("tenant2").Database.Identifier.ShouldBe("database1");
        theTenancy.GetTenant("tenant3").Database.Identifier.ShouldBe("database1");
        theTenancy.GetTenant("tenant4").Database.Identifier.ShouldBe("database2");
        theTenancy.GetTenant("tenant5").Database.Identifier.ShouldBe("database2");
    }
}
