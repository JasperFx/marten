#nullable enable
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// #5269. <c>StoreOptions.CommandTimeout</c> is store-wide and was only ever raised from the connection string
/// handed to <c>StoreOptions.Connection(string)</c>. Under multi-database tenancy that method never runs, so a
/// <c>Command Timeout</c> on every shard connection string was silently ignored and every batch kept the five
/// second default — in the report behind this issue, a 215-operation batch against a loaded shard whose own
/// connection string asked for 300.
/// </summary>
public class Bug_5269_command_timeout_from_shard_connection_string
{
    private static string WithTimeout(int seconds) =>
        new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString) { CommandTimeout = seconds }
            .ConnectionString;

    private static string WithoutTimeout()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);
        builder.Remove("Command Timeout");
        return builder.ConnectionString;
    }

    private static DocumentStore StoreFor(string tenantConnectionString, int? explicitStoreTimeout = null)
    {
        return DocumentStore.For(opts =>
        {
            opts.MultiTenantedDatabases(x => x.AddSingleTenantDatabase(tenantConnectionString, "acme"));
            opts.DatabaseSchemaName = "bug_5269";
            opts.Policies.AllDocumentsAreMultiTenanted();

            if (explicitStoreTimeout.HasValue)
            {
                opts.CommandTimeout = explicitStoreTimeout.Value;
            }
        });
    }

    /// <summary>
    /// Asserts on what Marten actually stamps onto the command, rather than on any internal state.
    /// </summary>
    private static async Task<int> StampedTimeoutAsync(DocumentStore store, SessionOptions? options = null)
    {
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var session = options is null
            ? store.QuerySession("acme")
            : store.QuerySession(options);

        var command = await session.Query<Target>().ExplainAsync();
        return command.Command.CommandTimeout;
    }

    [Fact]
    public async Task the_shard_connection_string_is_honored()
    {
        using var store = StoreFor(WithTimeout(300));

        (await StampedTimeoutAsync(store)).ShouldBe(300);
    }

    [Fact]
    public async Task falls_back_to_the_store_default_when_the_shard_says_nothing()
    {
        using var store = StoreFor(WithoutTimeout());

        // Unchanged behaviour: no opinion anywhere means the store default.
        (await StampedTimeoutAsync(store)).ShouldBe(StoreOptions.DefaultTimeout);
    }

    [Fact]
    public async Task an_explicit_store_wide_value_still_wins_over_the_shard()
    {
        // Setting CommandTimeout deliberately is a store-wide instruction, and it must not be quietly
        // overridden by whatever a shard connection string happens to carry. This is also what keeps the
        // single-database path — where Connection(string) sets CommandTimeout itself — behaving as it did.
        using var store = StoreFor(WithTimeout(300), explicitStoreTimeout: 11);

        (await StampedTimeoutAsync(store)).ShouldBe(11);
    }

    [Fact]
    public async Task the_session_timeout_still_wins_over_everything()
    {
        using var store = StoreFor(WithTimeout(300));

        var stamped = await StampedTimeoutAsync(store, new SessionOptions { TenantId = "acme", Timeout = 7 });
        stamped.ShouldBe(7);
    }

    [Fact]
    public async Task a_database_reports_the_timeout_its_own_connection_string_asks_for()
    {
        using var store = StoreFor(WithTimeout(123));
        var database = (IMartenDatabase)await store.Storage.FindOrCreateDatabase("acme");

        database.ConfiguredCommandTimeout.ShouldBe(123);
    }

    [Fact]
    public async Task a_database_reports_null_when_its_connection_string_is_silent()
    {
        // Npgsql's builder hands back its own default of 30 for a string that never mentioned a timeout, so
        // "unspecified" has to stay distinguishable from "deliberately 30" or the store default is unreachable.
        using var store = StoreFor(WithoutTimeout());
        var database = (IMartenDatabase)await store.Storage.FindOrCreateDatabase("acme");

        database.ConfiguredCommandTimeout.ShouldBeNull();
    }
}
