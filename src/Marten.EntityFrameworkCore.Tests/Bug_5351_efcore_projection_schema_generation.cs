using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

#region 5351 model

/// <summary>
/// The heart of the second half of #5351: EF Core is perfectly happy with a primary key that is
/// not called <c>Id</c>, but Marten's document conventions are not. This type must never reach
/// them.
/// </summary>
public class ShipmentTracking
{
    public Guid TrackingNumber { get; set; }
    public string Destination { get; set; } = string.Empty;
    public int PackageCount { get; set; }
}

public class ShipmentDbContext: DbContext
{
    public ShipmentDbContext(DbContextOptions<ShipmentDbContext> options): base(options)
    {
    }

    public DbSet<ShipmentTracking> Shipments => Set<ShipmentTracking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShipmentTracking>(entity =>
        {
            entity.ToTable("ef_shipment_tracking");

            // Deliberately NOT named Id.
            entity.HasKey(e => e.TrackingNumber);
            entity.Property(e => e.TrackingNumber).HasColumnName("tracking_number");
            entity.Property(e => e.Destination).HasColumnName("destination");
            entity.Property(e => e.PackageCount).HasColumnName("package_count");
        });
    }
}

public record ShipmentDispatched(string Destination);

public record PackageLoaded;

public class ShipmentTrackingProjection
    : EfCoreSingleStreamProjection<ShipmentTracking, Guid, ShipmentDbContext>
{
    public override ShipmentTracking? ApplyEvent(ShipmentTracking? snapshot, Guid identity,
        IEvent @event, ShipmentDbContext dbContext, IQuerySession session)
    {
        snapshot ??= new ShipmentTracking { TrackingNumber = identity };

        switch (@event.Data)
        {
            case ShipmentDispatched dispatched:
                snapshot.Destination = dispatched.Destination;
                break;

            case PackageLoaded:
                snapshot.PackageCount++;
                break;
        }

        return snapshot;
    }
}

/// <summary>
/// A perfectly ordinary Marten-stored projection over the same events, sharing every store below.
/// It exists to prove the fix did not go too far the other way — its <c>mt_doc_</c> table must
/// still be created, asserted and migrated. It applies events by overriding
/// <c>DetermineActionAsync</c> only because this test project does not wire the projection source
/// generator; what matters here is where its data lives.
/// </summary>
public class ShipmentAudit
{
    public Guid Id { get; set; }
    public int EventCount { get; set; }
}

public class ShipmentAuditProjection: SingleStreamProjection<ShipmentAudit, Guid>
{
    public override ValueTask<(ShipmentAudit?, ActionType)> DetermineActionAsync(
        IQuerySession session,
        ShipmentAudit? snapshot,
        Guid identity,
        IIdentitySetter<ShipmentAudit, Guid> identitySetter,
        IReadOnlyList<IEvent> events,
        CancellationToken cancellation)
    {
        snapshot ??= new ShipmentAudit { Id = identity };
        snapshot.EventCount += events.Count;

        return new ValueTask<(ShipmentAudit?, ActionType)>((snapshot, ActionType.Store));
    }
}

#endregion

/// <summary>
/// #5351: registering an EF Core projection made Marten build a <c>DocumentMapping</c> for the
/// aggregate type and emit schema for it, even though the rows live in a DbContext-mapped table
/// that Marten never reads or writes.
///
/// <list type="number">
/// <item><b>A useless table, and a false "missing" report.</b>
/// <c>ApplyAllConfiguredChangesToDatabaseAsync()</c> created an empty <c>mt_doc_&lt;tdoc&gt;</c>
/// that nothing uses, and under the ordinary <see cref="AutoCreate.None"/> deployment shape —
/// schema built by a migration pass that does not register the projection —
/// <c>AssertDatabaseMatchesConfigurationAsync()</c> then reported that table MISSING, so a
/// correctly migrated database failed its own startup assertion.</item>
/// <item><b>A hard throw for any entity whose key is not named <c>Id</c>.</b>
/// <c>DocumentTable</c>'s constructor validates unconditionally, so
/// <c>InvalidDocumentException: Could not determine an 'id/Id' field or property</c> came out of
/// <i>every</i> full-schema operation, including <c>db-apply</c> and <c>db-patch</c>.</item>
/// </list>
///
/// The type arrives through <c>ProjectionBase.PublishedTypes()</c>, which
/// <c>JasperFxAggregationProjectionBase</c> feeds via <c>RegisterPublishedType(typeof(TDoc))</c>
/// independently of <c>AsyncOptions.StorageTypes</c> — so trimming that list changes nothing, and
/// narrowing the traversal is off the table (#5169). The fix marks the mapping
/// <c>SkipSchemaGeneration</c> instead, keyed on <c>StoreOptions.CustomProjectionStorageProviders</c>
/// — the same registry rebuild teardown consults since #5329/#5350.
///
/// Every test carries <see cref="ShipmentAuditProjection"/>, a conventional Marten projection, in
/// the same store, so the guard against over-reaching is live throughout rather than sitting in
/// one test at the bottom.
/// </summary>
public class Bug_5351_efcore_projection_schema_generation: IAsyncLifetime
{
    private const string SchemaName = "efcore_schema_5351";

    public async ValueTask InitializeAsync()
    {
        await dropSchemaAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await dropSchemaAsync();
    }

    /// <summary>
    /// The application store: the EF Core projection IS registered, alongside a conventional
    /// Marten projection.
    /// </summary>
    private static DocumentStore buildProjectionStore(AutoCreate autoCreate = AutoCreate.All)
    {
        return DocumentStore.For(opts => configureProjectionStore(opts, autoCreate));
    }

    private static void configureProjectionStore(StoreOptions opts, AutoCreate autoCreate)
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = SchemaName;
        opts.AutoCreateSchemaObjects = autoCreate;
        opts.Events.AddEventType<ShipmentDispatched>();
        opts.Events.AddEventType<PackageLoaded>();

        opts.Add(new ShipmentTrackingProjection(), ProjectionLifecycle.Inline);
        opts.Projections.Add(new ShipmentAuditProjection(), ProjectionLifecycle.Inline);
    }

    /// <summary>
    /// The migration store: the reporter's deployment shape. It knows about the EF Core entity
    /// tables and the event store but does NOT register the EF Core projection, which is exactly
    /// why the existing #5329 suite does not cover this defect.
    /// </summary>
    private static DocumentStore buildMigrationStore()
    {
        return DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.AddEventType<ShipmentDispatched>();
            opts.Events.AddEventType<PackageLoaded>();
            opts.AddEntityTablesFromDbContext<ShipmentDbContext>();

            opts.Projections.Add(new ShipmentAuditProjection(), ProjectionLifecycle.Inline);
        });
    }

    [Fact]
    public async Task apply_all_configured_changes_creates_no_marten_table_for_the_ef_aggregate()
    {
        using var store = buildProjectionStore();

        // Before the fix this threw InvalidDocumentException, because ShipmentTracking is keyed on
        // TrackingNumber rather than Id.
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await tableExistsAsync("ef_shipment_tracking")).ShouldBeTrue();
        (await tableExistsAsync("mt_doc_shipmenttracking")).ShouldBeFalse();

        // ...and the guard: a conventional Marten projection still gets its document table.
        (await tableExistsAsync("mt_doc_shipmentaudit")).ShouldBeTrue();
    }

    [Fact]
    public async Task assert_database_matches_configuration_passes_against_a_migration_built_schema()
    {
        // Stand the schema up from a store that never heard of the EF Core projection.
        using (var migrationStore = buildMigrationStore())
        {
            await migrationStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        // The application store then asserts against it at startup. Before the fix this failed:
        // either with InvalidDocumentException while building the schema objects, or — for an
        // Id-keyed entity — with mt_doc_<tdoc> reported MISSING.
        using var store = buildProjectionStore(AutoCreate.None);
        await Should.NotThrowAsync(() => store.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
    }

    [Fact]
    public async Task every_full_schema_operation_survives_a_non_id_keyed_entity()
    {
        using var store = buildProjectionStore();

        // Each of these routes through MartenDatabase.BuildFeatureSchemas -> AllActiveFeatures,
        // and each threw InvalidDocumentException before the fix.
        await Should.NotThrowAsync(() => store.Storage.ApplyAllConfiguredChangesToDatabaseAsync());
        await Should.NotThrowAsync(() => store.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
        await Should.NotThrowAsync(() => store.Storage.Database.CreateMigrationAsync());
        await Should.NotThrowAsync(() => store.Advanced.ResetAllData());

        // A migration script for this store must not mention the phantom table either — that is
        // what gets checked into a deployment repo and run everywhere.
        var migration = await store.Storage.Database.CreateMigrationAsync();
        var sql = store.Storage.Database.ToDatabaseScript();
        sql.ShouldNotContain("mt_doc_shipmenttracking", Case.Insensitive);
        sql.ShouldContain("mt_doc_shipmentaudit", Case.Insensitive);
        migration.ShouldNotBeNull();
    }

    /// <summary>
    /// Symptom 1 on its own. Every other test here uses <see cref="ShipmentTracking"/>, whose
    /// non-<c>Id</c> key makes the throw arrive first and hide what happens after it. This one
    /// uses the conventionally keyed <see cref="CustomerOrderHistory"/>, where Marten was perfectly
    /// capable of building the phantom table — and did, on every
    /// <c>ApplyAllConfiguredChangesToDatabaseAsync()</c>.
    /// </summary>
    [Fact]
    public async Task no_phantom_document_table_even_when_the_ef_entity_is_keyed_on_id()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AddEventType<CustomerOrderPlaced>();
            opts.Events.AddEventType<CustomerOrderCompleted>();

            opts.Add(new CustomerOrderHistoryProjection(), ProjectionLifecycle.Inline);
        });

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await tableExistsAsync("ef_customer_order_histories")).ShouldBeTrue();
        (await tableExistsAsync("mt_doc_customerorderhistory")).ShouldBeFalse();

        // And once it is gone, the AutoCreate.None assertion against a migration-built schema
        // stops reporting it MISSING — the reporter's actual startup failure.
        using var deployed = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.AddEventType<CustomerOrderPlaced>();
            opts.Events.AddEventType<CustomerOrderCompleted>();

            opts.Add(new CustomerOrderHistoryProjection(), ProjectionLifecycle.Inline);
        });

        await Should.NotThrowAsync(() => deployed.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
    }

    [Fact]
    public async Task db_apply_then_db_assert_from_the_command_line()
    {
        // The CLI is where the reporter met this: db-apply and db-patch both walk every schema
        // object, so an entity keyed on anything but Id took the whole command down.
        var apply = await hostBuilder().RunJasperFxCommands(["db-apply"]);
        apply.ShouldBe(0);

        var assert = await hostBuilder().RunJasperFxCommands(["db-assert"]);
        assert.ShouldBe(0);

        (await tableExistsAsync("ef_shipment_tracking")).ShouldBeTrue();
        (await tableExistsAsync("mt_doc_shipmenttracking")).ShouldBeFalse();
        (await tableExistsAsync("mt_doc_shipmentaudit")).ShouldBeTrue();
    }

    private static IHostBuilder hostBuilder()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts => configureProjectionStore(opts, AutoCreate.All));
            });
    }

    /// <summary>
    /// The whole point of skipping schema generation is that Marten was never going to use that
    /// table — so the projection has to keep working, and the conventional projection alongside it
    /// has to keep landing in Marten document storage.
    /// </summary>
    [Fact]
    public async Task both_projections_still_persist_where_they_belong()
    {
        using var store = buildProjectionStore();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var streamId = Guid.NewGuid();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new ShipmentDispatched("Austin"), new PackageLoaded(), new PackageLoaded());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var (destination, packageCount) = await readShipmentAsync(streamId);
        destination.ShouldBe("Austin");
        packageCount.ShouldBe(2);

        await using var query = store.QuerySession();
        var audit = await query.LoadAsync<ShipmentAudit>(streamId, TestContext.Current.CancellationToken);
        audit.ShouldNotBeNull();
        audit.EventCount.ShouldBe(3);
    }

    private static async Task<bool> tableExistsAsync(string tableName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select to_regclass(@name) is not null";
        cmd.Parameters.AddWithValue("name", $"{SchemaName}.{tableName}");
        return (bool)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static async Task<(string Destination, int PackageCount)> readShipmentAsync(Guid trackingNumber)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select destination, package_count from {SchemaName}.ef_shipment_tracking where tracking_number = @id";
        cmd.Parameters.AddWithValue("id", trackingNumber);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();
        return (reader.GetString(0), reader.GetInt32(1));
    }

    private static async Task dropSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP SCHEMA IF EXISTS {SchemaName} CASCADE";
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
