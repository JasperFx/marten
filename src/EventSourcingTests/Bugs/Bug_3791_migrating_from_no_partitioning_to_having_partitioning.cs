using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using EventSourcingTests.Aggregation;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
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

    /// <summary>
    /// #5812 item 9: the upgrade an existing Marten 9 store gets from the 10.0 default. Polecat lost every event
    /// when it switched archived-stream partitioning on for an existing store under AutoCreate.All, and threw under
    /// CreateOrUpdate, so this proves both modes, through an explicit migration and through the implicit one the
    /// first session triggers, keep every event, stream and archived flag.
    /// </summary>
    [Theory]
    [InlineData(AutoCreate.CreateOrUpdate, true)]
    [InlineData(AutoCreate.CreateOrUpdate, false)]
    [InlineData(AutoCreate.All, true)]
    [InlineData(AutoCreate.All, false)]
    public async Task an_existing_marten_9_store_migrates_to_the_partitioned_layout_without_losing_events(
        AutoCreate autoCreate, bool explicitMigration)
    {
        var schema = $"bug_3791_{autoCreate.ToString().ToLowerInvariant()}_{(explicitMigration ? "apply" : "implicit")}";

        // A Marten 9 shaped event store: no archived partitioning, events in several streams, one stream archived
        StoreOptions(o =>
        {
            o.Events.UseArchivedStreamPartitioning = false;
            o.Events.EnableStrictStreamIdentityEnforcement = false;
            o.DatabaseSchemaName = schema;
        });

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var live = Guid.NewGuid();
        var archived = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(live, new AEvent(), new BEvent(), new CEvent());
            session.Events.StartStream(archived, new AEvent(), new BEvent());
            for (var i = 0; i < 20; i++) session.Events.StartStream(Guid.NewGuid(), new AEvent(), new DEvent());
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.ArchiveStream(archived);
            await session.SaveChangesAsync();
        }

        var before = await countAsync(schema);
        before.Events.ShouldBe(45);
        before.Streams.ShouldBe(22);

        // The same database under the 10.0 defaults
        using var store2 = SeparateStore(o =>
        {
            o.DatabaseSchemaName = schema;
            o.AutoCreateSchemaObjects = autoCreate;
        });

        if (explicitMigration)
        {
            await store2.Storage.ApplyAllConfiguredChangesToDatabaseAsync(autoCreate);
        }

        // The first session migrates the event store implicitly when nothing was applied above
        await using (var session = store2.LightweightSession())
        {
            session.Events.Append(live, new DEvent());
            await session.SaveChangesAsync();
        }

        var after = await countAsync(schema);
        after.Events.ShouldBe(46);
        after.Streams.ShouldBe(22);
        after.ArchivedEvents.ShouldBe(2);
        after.Partitioned.ShouldBeTrue();

        await using var query = store2.QuerySession();
        (await query.Events.FetchStreamAsync(live)).Select(x => x.Version).ShouldBe([1L, 2L, 3L, 4L]);
        (await query.Events.FetchStreamStateAsync(archived))!.IsArchived.ShouldBeTrue();

        await store2.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    private static async Task<(long Events, long Streams, long ArchivedEvents, bool Partitioned)> countAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
select (select count(*) from {schema}.mt_events),
       (select count(*) from {schema}.mt_streams),
       (select count(*) from {schema}.mt_events where is_archived),
       exists (select 1 from pg_partitioned_table p join pg_class c on c.oid = p.partrelid
                 join pg_namespace n on n.oid = c.relnamespace
                where n.nspname = '{schema}' and c.relname = 'mt_events')";
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetBoolean(3));
    }
}
