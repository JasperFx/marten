using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Daemon;

/// <summary>
/// #5740 / jasperfx#809: Marten's own side of the persisted projection start floor. The seam's contract is
/// pinned by <c>ProjectionFloorCompliance</c>; what is asserted here is what only Marten can get wrong: the
/// table's place in schema management, which resets leave it alone, tenant id correction, and that a database
/// whose schema predates the table keeps starting its shards.
/// </summary>
public class projection_floor_storage: OneOffConfigurationsContext
{
    private static string AProjection() => "Floor" + Guid.NewGuid().ToString("N")[..12];

    private IEventDatabase Database => (IEventDatabase)theStore.Storage.Database;

    [Fact]
    public async Task the_floor_table_is_part_of_the_event_store_schema_and_does_not_drift()
    {
        StoreOptions(opts => opts.Events.AddEventType<FloorEvent>());

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var tables = await theStore.Storage.Database.SchemaTables();
        tables.Select(x => x.Name).ShouldContain("mt_event_projection_floors");

        // A table that re-introspects differently from how it is declared is a migration on every start
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }

    [Fact]
    public async Task a_database_without_the_floor_table_reports_no_floor_instead_of_failing()
    {
        // The daemon reads the floor on every shard start and every rebuild. A schema migrated before the
        // table existed has to keep starting shards exactly as it did, so the read answers null (#5511).
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<FloorEvent>();
            opts.AutoCreateSchemaObjects = AutoCreate.None;
        });

        // Dropped explicitly: the schema is shared by this class's tests, so another one may have created it
        await using (var conn = theStore.Storage.Database.CreateConnection())
        {
            await conn.OpenAsync();
            await conn.CreateCommand($"drop table if exists {SchemaName}.mt_event_projection_floors")
                .ExecuteNonQueryAsync();
        }

        (await Database.FetchProjectionFloorAsync(AProjection(), null)).ShouldBeNull();
    }

    [Fact]
    public async Task a_floor_outlives_every_data_reset_but_not_complete_removal()
    {
        StoreOptions(opts => opts.Events.AddEventType<FloorEvent>());
        var projection = AProjection();
        await Database.TrySetProjectionFloorAsync(projection, null, 500);

        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
        await theStore.Advanced.Clean.DeleteAllDocumentsAsync();
        await theStore.Advanced.ResetAllData();

        (await Database.FetchProjectionFloorAsync(projection, null)).ShouldBe(500);

        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();

        (await theStore.Storage.Database.SchemaTables()).Select(x => x.Name)
            .ShouldNotContain("mt_event_projection_floors");
    }

    [Fact]
    public async Task tenant_ids_are_corrected_like_every_other_tenant_id()
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<FloorEvent>();
            opts.TenantIdStyle = TenantIdStyle.ForceLowerCase;
        });
        var projection = AProjection();

        await Database.SetProjectionFloorAsync(projection, "ACME", 42);

        (await Database.FetchProjectionFloorAsync(projection, "acme")).ShouldBe(42);
        (await Database.TrySetProjectionFloorAsync(projection, "Acme", 99)).ShouldBe(42);
    }

    [Fact]
    public async Task an_operator_clear_of_a_floor_that_was_never_set_is_a_no_op()
    {
        StoreOptions(opts => opts.Events.AddEventType<FloorEvent>());
        var projection = AProjection();

        await Database.SetProjectionFloorAsync(projection, null, null);

        (await Database.FetchProjectionFloorAsync(projection, null)).ShouldBeNull();
    }
}

public record FloorEvent(string Name);
