using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using EventSourcingTests.Aggregation;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3791_migrating_from_no_partitioning_to_having_partitioning : BugIntegrationContext
{
    [Fact]
    public async Task try_it_out()
    {
        StoreOptions(o =>
        {
            // The Marten 9 shape. Archived-stream partitioning is the default since 10.0 (#5812).
            o.Events.UseArchivedStreamPartitioning = false;
            o.Events.EnableStrictStreamIdentityEnforcement = false;
            o.DisableNpgsqlLogging = true;
            o.Events.UseOptimizedProjectionRebuilds = true;
            o.Events.AppendMode = EventAppendMode.Quick;
            o.Events.UseIdentityMapForAggregates = true;
            o.DatabaseSchemaName = "some_custom_schema";
            o.UseSystemTextJsonForSerialization(EnumStorage.AsString, Casing.CamelCase, c =>
            {
                c.PropertyNameCaseInsensitive = true;
            });
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(o =>
        {
            o.Events.UseArchivedStreamPartitioning = true;
            o.DisableNpgsqlLogging = true;
            o.Events.UseOptimizedProjectionRebuilds = true;
            o.Events.AppendMode = EventAppendMode.Quick;
            o.Events.UseIdentityMapForAggregates = true;
            o.DatabaseSchemaName = "some_custom_schema";
            o.UseSystemTextJsonForSerialization(EnumStorage.AsString, Casing.CamelCase, c =>
            {
                c.PropertyNameCaseInsensitive = true;
            });
        });
        
        await store2.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    [Fact]
    public async Task an_existing_event_store_keeps_its_events_and_keeps_appending_after_the_migration()
    {
        // #5812: the upgrade path to the 10.0 default. A Marten 9 shaped event store with events in it...
        StoreOptions(o =>
        {
            o.Events.UseArchivedStreamPartitioning = false;
            o.Events.EnableStrictStreamIdentityEnforcement = false;
            o.DatabaseSchemaName = "bug_3791_upgrade";
        });

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var existing = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(existing, new AEvent(), new BEvent());
            await session.SaveChangesAsync();
        }

        // ...then the same schema under the 10.0 defaults
        using var store2 = SeparateStore(o => o.DatabaseSchemaName = "bug_3791_upgrade");
        await store2.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using (var session = store2.LightweightSession())
        {
            session.Events.Append(existing, new CEvent());
            session.Events.StartStream(Guid.NewGuid(), new AEvent());
            await session.SaveChangesAsync();
        }

        await using var query = store2.QuerySession();
        var events = await query.Events.FetchStreamAsync(existing);
        events.Select(x => x.Version).ShouldBe([1L, 2L, 3L]);
        events.Select(x => x.Sequence).ShouldBe([1L, 2L, 3L]);

        await store2.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}
