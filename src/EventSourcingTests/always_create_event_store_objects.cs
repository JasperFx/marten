using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests;

// #5765: the cheap event-store objects exist whatever the flags say, so two stores on one schema with
// different settings cannot drop each other's objects (#5309). The flags still govern behavior.
public class always_create_event_store_objects : OneOffConfigurationsContext
{
    [Fact]
    public async Task cheap_objects_exist_with_every_flag_off()
    {
        StoreOptions(opts =>
        {
            opts.Events.EnableAdvancedAsyncTracking = false;
            opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = false;
            opts.Events.MetadataConfig.CorrelationIdEnabled = false;
            opts.Events.MetadataConfig.CausationIdEnabled = false;
            opts.Events.MetadataConfig.HeadersEnabled = false;
            opts.Events.MetadataConfig.UserNameEnabled = false;
        });

        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tables = await theStore.Storage.Database.SchemaTables();
        tables.ShouldContain(x => x.Name == "mt_high_water_skips");

        var functions = await theStore.Storage.Database.Functions();
        functions.ShouldContain(new DbObjectName(SchemaName, "mt_mark_progression_with_skip"));

        await using var session = theStore.QuerySession();
        var columns = await session.QueryAsync<string>(
            "select column_name from information_schema.columns where table_schema = ? and table_name = 'mt_events'",
            SchemaName);
        foreach (var column in new[] { "is_skipped", "correlation_id", "causation_id", "headers", "user_name" })
        {
            columns.ShouldContain(column);
        }

        // Nothing to migrate afterwards: the flags-off store does not see these as extras
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task a_flags_off_store_does_not_drop_what_a_flags_on_store_created()
    {
        StoreOptions(opts =>
        {
            opts.Events.EnableAdvancedAsyncTracking = true;
            opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = true;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
            opts.Events.MetadataConfig.HeadersEnabled = true;
            opts.Events.MetadataConfig.UserNameEnabled = true;
        });
        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var off = SeparateStore(opts =>
        {
            opts.Events.EnableAdvancedAsyncTracking = false;
            opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = false;
        });

        await off.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
        await off.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await off.Storage.Database.AssertDatabaseMatchesConfigurationAsync();

        // and the original store still matches
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}
