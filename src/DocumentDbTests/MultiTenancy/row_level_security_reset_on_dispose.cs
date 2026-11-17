using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DocumentDbTests.MultiTenancy;

/// <summary>
///     #5861 / #5907. A synchronous <c>Dispose()</c> on a row-level-security session resets the tenant setting with one
///     synchronous call before the connection goes back to the pool. Npgsql's own reset-on-close would hide a missing
///     reset, so every store here turns it off (<c>No Reset On Close=true</c>). A pool of one makes the "next" connection
///     the same physical one, and each test checks that by backend pid, so a test cannot pass vacuously on a fresh
///     connection.
/// </summary>
public class row_level_security_reset_on_dispose: OneOffConfigurationsContext
{
    private const string Tenant = "tenant_a";

    public row_level_security_reset_on_dispose()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        _schemaName = $"{GetType().Name}_{suffix}".ToLowerInvariant();
    }

    private async Task<DocumentStore> rlsStoreWithoutResetOnClose()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            NoResetOnClose = true, MaxPoolSize = 1
        };

        var store = StoreOptions(opts =>
        {
            opts.Connection(builder.ConnectionString);
            opts.Schema.For<Target>().MultiTenanted();
            opts.UseRowLevelSecurity();
        });

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        return store;
    }

    private static async Task<(string Setting, int Pid)> readFromTheSamePool(IDocumentStore store)
    {
        await using var conn = store.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select current_setting('app.tenant_id', true), pg_backend_pid()";
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        var setting = await reader.IsDBNullAsync(0) ? null : reader.GetString(0);
        return (setting, reader.GetInt32(1));
    }

    private static async Task<int> openAsTenant(IQuerySession session)
    {
        (await session.QueryAsync<string>("select current_setting('app.tenant_id')")).Single().ShouldBe(Tenant);
        return (await session.GetConnectionAsync()).ProcessID;
    }

    [Fact]
    public async Task synchronous_dispose_resets_the_tenant_setting_on_the_pooled_connection()
    {
        var store = await rlsStoreWithoutResetOnClose();

        var session = store.QuerySession(Tenant);
        var pid = await openAsTenant(session);

        session.Dispose();

        var (setting, nextPid) = await readFromTheSamePool(store);
        nextPid.ShouldBe(pid, "the pool of one should hand back the same physical connection");
        setting.ShouldNotBe(Tenant);
    }

    [Fact]
    public async Task synchronous_dispose_of_a_document_session_resets_the_tenant_setting()
    {
        var store = await rlsStoreWithoutResetOnClose();

        var session = store.LightweightSession(Tenant);
        session.Store(new Target { Number = 1 });
        await session.SaveChangesAsync();

        // The commit releases the connection (and resets it asynchronously), so open it again before disposing
        var pid = await openAsTenant(session);

        session.Dispose();

        var (setting, nextPid) = await readFromTheSamePool(store);
        nextPid.ShouldBe(pid);
        setting.ShouldNotBe(Tenant);
    }

    [Fact]
    public async Task asynchronous_dispose_still_resets_the_tenant_setting()
    {
        var store = await rlsStoreWithoutResetOnClose();

        var session = store.QuerySession(Tenant);
        var pid = await openAsTenant(session);

        await session.DisposeAsync();

        var (setting, nextPid) = await readFromTheSamePool(store);
        nextPid.ShouldBe(pid);
        setting.ShouldNotBe(Tenant);
    }

    [Fact]
    public async Task a_caller_supplied_connection_is_left_alone_by_synchronous_dispose()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<Target>().MultiTenanted();
            opts.UseRowLevelSecurity();
        });

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var sessionOptions = SessionOptions.ForTransaction(tx);
        sessionOptions.TenantId = Tenant;

        var session = theStore.QuerySession(sessionOptions);
        (await session.QueryAsync<string>("select current_setting('app.tenant_id')")).Single().ShouldBe(Tenant);

        session.Dispose();

        // The connection is the caller's, so Marten makes no synchronous call on it. DisposeAsync would reset it.
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select current_setting('app.tenant_id', true)";
        cmd.Transaction = tx;
        (await cmd.ExecuteScalarAsync()).ShouldBe(Tenant);
    }

    [Fact]
    public async Task a_failed_reset_does_not_throw_and_does_not_return_the_connection_to_the_pool()
    {
        var store = await rlsStoreWithoutResetOnClose();

        var session = store.QuerySession(Tenant);
        var pid = await openAsTenant(session);

        // Kill the backend under the session, so the synchronous reset fails
        var killer = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString) { Pooling = false };
        await using (var conn = new NpgsqlConnection(killer.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"select pg_terminate_backend({pid})";
            await cmd.ExecuteScalarAsync();
        }

        Should.NotThrow(() => session.Dispose());

        var (setting, nextPid) = await readFromTheSamePool(store);
        nextPid.ShouldNotBe(pid, "the broken connection must not have gone back to the pool");
        setting.ShouldNotBe(Tenant);
    }
}
