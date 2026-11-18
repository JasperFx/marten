using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// #6004 on a database-per-tenant store: every database has its own sequence, so the position carries the
/// database, and the wait reads that database's progression table and no other.
/// </summary>
[Collection("multi-tenancy")]
public class read_your_writes_per_database: IAsyncLifetime
{
    // One set of databases per target framework, because the assembly runs once per framework concurrently
    private static readonly string Suffix = $"_net{Environment.Version.Major}";
    private static readonly string TenantA = $"ryw6004_tenant_a{Suffix}";
    private static readonly string TenantB = $"ryw6004_tenant_b{Suffix}";
    private static readonly string MasterSchema = $"ryw6004_master{Suffix}";
    private static readonly string SchemaName = $"ryw6004{Suffix}";

    private IDocumentStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.DropSchemaAsync(MasterSchema);
        var tenantA = await TenantDatabases.CreateIfNotExistsAsync(conn, TenantA);
        var tenantB = await TenantDatabases.CreateIfNotExistsAsync(conn, TenantB);
        await conn.CloseAsync();

        await dropSchemaAsync(tenantA);
        await dropSchemaAsync(tenantB);

        _store = DocumentStore.For(opts =>
        {
            opts.MultiTenantedDatabasesWithMasterDatabaseTable(configure =>
            {
                configure.ConnectionString = ConnectionSource.ConnectionString;
                configure.SchemaName = MasterSchema;
                configure.RegisterDatabase("tenant-a", tenantA);
                configure.RegisterDatabase("tenant-b", tenantB);
            });

            opts.DatabaseSchemaName = SchemaName;
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.DisableNpgsqlLogging = true;
            opts.Projections.Add<RywWidgetTallyProjection>(ProjectionLifecycle.Async);
        });

        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public ValueTask DisposeAsync()
    {
        _store.Dispose();
        return default;
    }

    private static async Task dropSchemaAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await conn.DropSchemaAsync(SchemaName);
        await conn.CloseAsync();
    }

    private async Task<EventStorePosition> appendAsync(string tenantId, int count)
    {
        await using var session = _store.LightweightSession(tenantId);
        for (var i = 0; i < count; i++)
        {
            session.Events.StartStream(Guid.NewGuid(), new RywWidgetMade(i));
        }

        await session.SaveChangesAsync();

        return session.LastAppendedPosition.ShouldNotBeNull();
    }

    [Fact]
    public async Task the_position_names_the_tenants_database()
    {
        var a = await appendAsync("tenant-a", 3);
        var b = await appendAsync("tenant-b", 1);

        var databaseA = await _store.Storage.FindOrCreateDatabase("tenant-a");
        var databaseB = await _store.Storage.FindOrCreateDatabase("tenant-b");

        a.DatabaseIdentifier.ShouldBe(databaseA.Identifier);
        a.TenantId.ShouldBe("tenant-a");
        a.Sequence.ShouldBe(3);

        b.DatabaseIdentifier.ShouldBe(databaseB.Identifier);
        b.Sequence.ShouldBe(1);
    }

    [Fact]
    public async Task each_database_is_waited_on_by_its_own_progression()
    {
        var a = await appendAsync("tenant-a", 3);
        var b = await appendAsync("tenant-b", 1);

        // Only tenant a's database is projected
        using (var daemon = await _store.BuildProjectionDaemonAsync("tenant-a"))
        {
            await daemon.StartAllAsync();

            var forA = await _store.WaitForProjectionsAsync(a, [typeof(RywWidgetTally)], 30.Seconds(),
                TestContext.Current.CancellationToken);
            forA.Reached.ShouldBeTrue(forA.ToString());

            await daemon.StopAllAsync();
        }

        // Tenant a's database is at 3, which is past b's sequence of 1. That must not count for b.
        var forB = await _store.WaitForProjectionsAsync(b, [typeof(RywWidgetTally)], 300.Milliseconds(),
            TestContext.Current.CancellationToken);
        forB.TimedOut.ShouldBeTrue(forB.ToString());
        forB.Shards.Single().Sequence.ShouldBeNull();

        using (var daemon = await _store.BuildProjectionDaemonAsync("tenant-b"))
        {
            await daemon.StartAllAsync();

            (await _store.WaitForProjectionsAsync(b, [typeof(RywWidgetTally)], 30.Seconds(),
                TestContext.Current.CancellationToken)).Reached.ShouldBeTrue();

            await daemon.StopAllAsync();
        }
    }

    [Fact]
    public async Task a_position_read_back_from_a_header_finds_its_database()
    {
        var a = await appendAsync("tenant-a", 2);

        using var daemon = await _store.BuildProjectionDaemonAsync("tenant-a");
        await daemon.StartAllAsync();

        var fromHeader = EventStorePosition.Parse(a.ToString());

        (await _store.WaitForProjectionsAsync(fromHeader, [typeof(RywWidgetTally)], 30.Seconds(),
            TestContext.Current.CancellationToken)).Reached.ShouldBeTrue();
    }
}

public record RywWidgetMade(int Number);

public class RywWidgetTally
{
    public string Id { get; set; } = "tally";
    public int Count { get; set; }
}

public partial class RywWidgetTallyProjection: MultiStreamProjection<RywWidgetTally, string>
{
    public RywWidgetTallyProjection()
    {
        Identity<RywWidgetMade>(_ => "tally");
    }

    public void Apply(RywWidgetMade _, RywWidgetTally tally) => tally.Count++;
}
