using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Rewriting;

/// <summary>
///     jasperfx#1215 / #1222: Marten's own side of the event-range marks. The seam's contract is pinned by
///     <c>EventRangeMarkCompliance</c>; this is the table's place in schema management, which resets leave it
///     alone, and tenant id correction.
/// </summary>
public class event_range_mark_storage: OneOffConfigurationsContext
{
    private static string AKey() => EventRangeMarks.RewriteKey("mark" + Guid.NewGuid().ToString("N")[..12]);

    private IEventDatabase Database => (IEventDatabase)theStore.Storage.Database;

    [Fact]
    public async Task the_mark_table_is_part_of_the_event_store_schema_and_does_not_drift()
    {
        StoreOptions(opts => opts.Events.AddEventType<MarkedEvent>());

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tables = await theStore.Storage.Database.SchemaTables();
        tables.Select(x => x.Name).ShouldContain("mt_event_range_marks");

        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task the_mark_table_is_there_with_the_marten_9_defaults_too()
    {
        StoreOptions(opts =>
        {
            opts.RestoreV9Defaults();
            opts.Events.AddEventType<MarkedEvent>();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await theStore.Storage.Database.SchemaTables()).Select(x => x.Name).ShouldContain("mt_event_range_marks");
    }

    [Fact]
    public async Task a_mark_outlives_every_data_reset_but_not_complete_removal()
    {
        StoreOptions(opts => opts.Events.AddEventType<MarkedEvent>());
        var key = AKey();
        await Database.TrySetEventRangeMarkAsync(key, null, 500);

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.ResetAllData();

        (await Database.FetchEventRangeMarkAsync(key, null)).ShouldBe(500);

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

        (await theStore.Storage.Database.SchemaTables()).Select(x => x.Name)
            .ShouldNotContain("mt_event_range_marks");
    }

    [Fact]
    public async Task tenant_ids_are_corrected_like_every_other_tenant_id()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<MarkedEvent>();
            opts.TenantIdStyle = TenantIdStyle.ForceLowerCase;
        });
        var key = AKey();

        await Database.SetEventRangeMarkAsync(key, "ACME", 42);

        (await Database.FetchEventRangeMarkAsync(key, "acme")).ShouldBe(42);
        (await Database.TrySetEventRangeMarkAsync(key, "Acme", 99)).ShouldBe(42);
    }
}

public record MarkedEvent(string Name);
