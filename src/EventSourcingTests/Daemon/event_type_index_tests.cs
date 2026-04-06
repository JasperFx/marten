using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Daemon;

public record TestEvent(string Value);

public class event_type_index_tests : OneOffConfigurationsContext
{
    [Fact]
    public async Task event_type_index_is_created_when_enabled()
    {
        StoreOptions(opts =>
        {
            opts.Events.EnableEventTypeIndex = true;
            opts.Events.AddEventType<TestEvent>();
        });

        // Ensure the event store schema is created
        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(JasperFx.Events.IEvent), default);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // Query pg_indexes to verify the index exists
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        var schema = theStore.Options.Events.DatabaseSchemaName;
        cmd.CommandText = $"SELECT indexname FROM pg_indexes WHERE schemaname = '{schema}' AND indexname LIKE '%event_type%'";
        var result = await cmd.ExecuteScalarAsync();

        result.ShouldNotBeNull(
            $"Expected event type index to exist in schema '{schema}'. " +
            "Enable opts.Events.EnableEventTypeIndex = true");
    }

    [Fact]
    public async Task event_type_index_is_not_created_by_default()
    {
        StoreOptions(opts =>
        {
            // Don't enable — this is the default
            opts.Events.AddEventType<TestEvent>();
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(JasperFx.Events.IEvent), default);
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        var schema = theStore.Options.Events.DatabaseSchemaName;
        cmd.CommandText = $"SELECT indexname FROM pg_indexes WHERE schemaname = '{schema}' AND indexname LIKE '%event_type%'";
        var result = await cmd.ExecuteScalarAsync();

        result.ShouldBeNull("Event type index should NOT be created by default");
    }
}
