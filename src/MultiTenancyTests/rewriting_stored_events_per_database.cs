using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
///     #5643 on a database-per-tenant store: every database is rewritten, and reports, on its own.
/// </summary>
[Collection("multi-tenancy")]
public class rewriting_stored_events_per_database: IAsyncLifetime
{
    public record ParcelSentV1(string To);

    public record ParcelSent(string Recipient);

    // One set of databases per target framework, because the assembly runs once per framework concurrently
    private static readonly string Suffix = $"_net{Environment.Version.Major}";
    private static readonly string TenantA = $"rw5643_tenant_a{Suffix}";
    private static readonly string TenantB = $"rw5643_tenant_b{Suffix}";
    private static readonly string MasterSchema = $"rw5643_master{Suffix}";
    private static readonly string SchemaName = $"rw5643{Suffix}";

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
            opts.Events.Upcast<ParcelSentV1, ParcelSent>(x => new ParcelSent(x.To));
        });

        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await appendAsync("tenant-a", 3);
        await appendAsync("tenant-b", 2);
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

    private async Task appendAsync(string tenantId, int count)
    {
        await using var session = _store.LightweightSession(tenantId);
        for (var i = 0; i < count; i++)
        {
            session.Events.StartStream(Guid.NewGuid(), new ParcelSentV1($"{tenantId} {i}"));
        }

        await session.SaveChangesAsync();
    }

    private async Task<string> databaseOf(string tenantId) =>
        (await _store.Storage.FindOrCreateDatabase(tenantId)).Identifier;

    [Fact]
    public async Task every_database_is_rewritten_and_reported_on_its_own()
    {
        var result = await _store.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.IncludeType<ParcelSent>();
            x.MaxParallelDatabases = 2;
            x.BatchSize = 2;
        }, TestContext.Current.CancellationToken);

        var databaseA = await databaseOf("tenant-a");
        var databaseB = await databaseOf("tenant-b");

        result.Databases.Count.ShouldBe(2);
        result.Databases.Single(x => x.Cursor.Database == databaseA).Rewritten.ShouldBe(3);
        result.Databases.Single(x => x.Cursor.Database == databaseB).Rewritten.ShouldBe(2);
        result.Databases.ShouldAllBe(x => x.Completed);
    }

    [Fact]
    public async Task one_tenant_is_its_database()
    {
        var result = await _store.Advanced.RewriteStoredEventsAsync(x => x
            .IncludeType<ParcelSent>()
            .ForTenant("tenant-b"), TestContext.Current.CancellationToken);

        var database = result.Databases.Single();
        database.Cursor.Database.ShouldBe(await databaseOf("tenant-b"));
        database.Rewritten.ShouldBe(2);

        // Tenant a's database was not touched
        var databaseA = await databaseOf("tenant-a");
        var rest = await _store.Advanced.RewriteStoredEventsAsync(x =>
        {
            x.IncludeType<ParcelSent>().ForDatabase(databaseA);
            x.DryRun = true;
        }, TestContext.Current.CancellationToken);
        rest.Databases.Single().Rewritten.ShouldBe(3);
    }

    [Fact]
    public async Task a_tenant_and_a_database_that_do_not_belong_together_are_refused()
    {
        var databaseA = await databaseOf("tenant-a");

        await Should.ThrowAsync<InvalidOperationException>(() =>
            _store.Advanced.RewriteStoredEventsAsync(x => x
                .IncludeType<ParcelSent>()
                .ForTenant("tenant-b")
                .ForDatabase(databaseA), TestContext.Current.CancellationToken));
    }
}
