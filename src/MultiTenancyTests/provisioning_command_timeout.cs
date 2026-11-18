using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;

namespace MultiTenancyTests;

/// <summary>
/// marten#6060: <c>CREATE DATABASE</c> and <c>DROP DATABASE</c> run under a provisioning command timeout, two minutes
/// by default, instead of the store's command timeout, which is tuned for queries. Every store here has a one second
/// command timeout.
/// </summary>
/// <remarks>
/// CREATE DATABASE is made slow by a second session holding a SHARE lock on <c>pg_database</c> for three seconds, which
/// CREATE DATABASE has to wait for to insert its row. Databases are server-wide, so every one here has a unique name and
/// is dropped afterwards.
/// </remarks>
[Collection("multi-tenancy")]
public class provisioning_command_timeout: IAsyncLifetime
{
    private readonly List<string> _created = [];
    private readonly List<IDocumentStore> _stores = [];

    private static readonly string OneSecondTimeout =
        new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString) { CommandTimeout = 1 }.ConnectionString;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var store in _stores) await store.DisposeAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.AdminConnectionString);
        await conn.OpenAsync();
        foreach (var name in _created)
        {
            await conn.KillIdleSessions(name);
            await conn.DropDatabase(name);
        }
    }

    private string newDatabaseName()
    {
        var name = "provisioning_" + Guid.NewGuid().ToString("N")[..12];
        _created.Add(name);
        return name;
    }

    private DocumentStore singleServerStore(Action<ISingleServerMultiTenancy>? configure = null)
    {
        var store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedWithSingleServer(OneSecondTimeout, configure);
            opts.DisableNpgsqlLogging = true;
        });

        _stores.Add(store);
        return store;
    }

    private DocumentStore storeForDatabase(string databaseName)
    {
        var store = DocumentStore.For(opts =>
        {
            opts.Connection(new NpgsqlConnectionStringBuilder(OneSecondTimeout) { Database = databaseName }.ConnectionString);
            opts.DisableNpgsqlLogging = true;
        });

        _stores.Add(store);
        return store;
    }

    private static async Task<bool> existsOnServer(string name)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.AdminConnectionString);
        await conn.OpenAsync();
        return await conn.DatabaseExists(name);
    }

    [Fact]
    public void the_single_server_tenancy_defaults_to_two_minutes()
    {
        var store = singleServerStore();

        ((ISingleServerMultiTenancy)store.Tenancy).ProvisioningCommandTimeout.ShouldBe(2.Minutes());
    }

    [Fact]
    public void the_single_server_tenancy_takes_its_configured_provisioning_timeout()
    {
        var store = singleServerStore(x => x.ProvisioningCommandTimeout = 5.Minutes());

        ((ISingleServerMultiTenancy)store.Tenancy).ProvisioningCommandTimeout.ShouldBe(5.Minutes());
    }

    [Fact]
    public async Task a_tenant_database_is_provisioned_under_a_one_second_store_command_timeout()
    {
        var store = singleServerStore();
        var name = newDatabaseName();

        var elapsed = await whileDatabaseCreationIsBlocked(() => store.Tenancy.GetTenantAsync(name).AsTask());

        elapsed.ShouldBeGreaterThan(1.Seconds());
        (await existsOnServer(name)).ShouldBeTrue();
    }

    [Fact]
    public async Task the_same_slow_tenant_database_times_out_with_a_one_second_provisioning_timeout()
    {
        // Proves the CREATE DATABASE above really is slow, so that test is not vacuous
        var store = singleServerStore(x => x.ProvisioningCommandTimeout = 1.Seconds());
        var name = newDatabaseName();

        await whileDatabaseCreationIsBlocked(async () =>
        {
            var ex = await Should.ThrowAsync<NpgsqlException>(() => store.Tenancy.GetTenantAsync(name).AsTask());
            ex.InnerException.ShouldBeOfType<TimeoutException>();
        });
    }

    [Fact]
    public async Task the_database_generator_creates_a_database_under_a_one_second_store_command_timeout()
    {
        var name = newDatabaseName();
        var store = storeForDatabase(name);

        var elapsed = await whileDatabaseCreationIsBlocked(() =>
            new DatabaseGenerator().CreateDatabasesAsync(store.Tenancy,
                x => x.MaintenanceDatabase(OneSecondTimeout).ForTenant()));

        elapsed.ShouldBeGreaterThan(1.Seconds());
        (await existsOnServer(name)).ShouldBeTrue();
    }

    [Fact]
    public async Task the_database_generator_times_out_with_a_one_second_provisioning_timeout()
    {
        var name = newDatabaseName();
        var store = storeForDatabase(name);

        await whileDatabaseCreationIsBlocked(async () =>
        {
            var ex = await Should.ThrowAsync<NpgsqlException>(() =>
                new DatabaseGenerator().CreateDatabasesAsync(store.Tenancy,
                    x => x.MaintenanceDatabase(OneSecondTimeout).ProvisioningCommandTimeout(1.Seconds()).ForTenant()));
            ex.InnerException.ShouldBeOfType<TimeoutException>();
        });
    }

    private static async Task<TimeSpan> whileDatabaseCreationIsBlocked(Func<Task> action)
    {
        await using var blocker = new NpgsqlConnection(ConnectionSource.AdminConnectionString);
        await blocker.OpenAsync();
        await using var tx = await blocker.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand("lock table pg_database in share mode", blocker, tx))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        var stopwatch = Stopwatch.StartNew();
        var running = action();

        // Long enough to sink a one second command timeout, and well short of two minutes
        var released = Task.Delay(3.Seconds()).ContinueWith(_ => tx.RollbackAsync()).Unwrap();

        try
        {
            await running;
        }
        finally
        {
            await released;
        }

        return stopwatch.Elapsed;
    }
}
