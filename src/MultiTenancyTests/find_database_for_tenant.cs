using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// #5658 — <see cref="IEventStore.FindDatabaseForTenantAsync"/> resolves a tenant through the store's own
/// tenancy, by lookup only. The JasperFx default answers null for anything but a one-database store, so
/// every multi-database case here would have fallen back to <c>AllDatabases()</c> without the override.
/// </summary>
[Collection("multi-tenancy")]
public class find_database_for_tenant
{
    private const string Known = "find_db_known";
    private const string NeverSeen = "find_db_never_seen";

    [Fact]
    public async Task single_server_resolves_a_known_tenant_to_its_own_database()
    {
        await DropDatabaseIfExists(Known);

        using var store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString, x => x.WithTenants(Known));
            opts.DisableNpgsqlLogging = true;
            opts.RegisterDocumentType<Target>();
        });

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var database = await ((IEventStore)store).FindDatabaseForTenantAsync(Known);

        database.ShouldNotBeNull();
        database.Identifier.ShouldBe(Known);
    }

    [Fact]
    public async Task single_server_answers_null_for_an_unknown_tenant_and_creates_nothing()
    {
        await DropDatabaseIfExists(NeverSeen);

        using var store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString, x => x.WithTenants(Known));
            opts.DisableNpgsqlLogging = true;
            opts.RegisterDocumentType<Target>();
        });

        (await ((IEventStore)store).FindDatabaseForTenantAsync(NeverSeen)).ShouldBeNull();

        // FindOrCreateDatabase would have issued CREATE DATABASE for this id
        (await DatabaseExists(NeverSeen)).ShouldBeFalse("a lookup must not provision a tenant database");
    }

    [Fact]
    public async Task static_multi_tenancy_resolves_known_tenants_and_answers_null_for_unknown()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var db1 = await TenantDatabases.CreateIfNotExistsAsync(conn, "database1");
        var tenant3 = await TenantDatabases.CreateIfNotExistsAsync(conn, "tenant3");

        using var store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedDatabases(x =>
            {
                x.AddMultipleTenantDatabase(db1, "database1").ForTenants("tenant1", "tenant2");
                x.AddSingleTenantDatabase(tenant3, "tenant3");
            });
            opts.DisableNpgsqlLogging = true;
        });

        var eventStore = (IEventStore)store;

        (await eventStore.FindDatabaseForTenantAsync("tenant1"))!.Identifier.ShouldBe("database1");
        (await eventStore.FindDatabaseForTenantAsync("tenant2"))!.Identifier.ShouldBe("database1");
        (await eventStore.FindDatabaseForTenantAsync("tenant3"))!.Identifier.ShouldBe("tenant3");

        // StaticMultiTenancy.FindOrCreateDatabase throws UnknownTenantIdException for this; the lookup answers null
        (await eventStore.FindDatabaseForTenantAsync("tenant99")).ShouldBeNull();
    }

    [Fact]
    public async Task conjoined_tenancy_resolves_every_tenant_to_the_one_database()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.DisableNpgsqlLogging = true;
        });

        var eventStore = (IEventStore)store;
        var theOne = store.Options.Tenancy.Default.Database;

        (await eventStore.FindDatabaseForTenantAsync("red")).ShouldBeSameAs(theOne);
        (await eventStore.FindDatabaseForTenantAsync("blue")).ShouldBeSameAs(theOne);
        (await eventStore.FindDatabaseForTenantAsync("never_registered")).ShouldBeSameAs(theOne);
    }

    private static async Task DropDatabaseIfExists(string databaseName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.KillIdleSessions(databaseName);
        await conn.DropDatabase(databaseName);
    }

    private static async Task<bool> DatabaseExists(string databaseName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        return await conn.DatabaseExists(databaseName);
    }
}
