using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Descriptors;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Migrations;
using Xunit;

namespace MultiTenancyTests;

#region test-projection

public record WidgetMade(string Name);

public class WidgetTally
{
    public string Id { get; set; } = "tally";
    public int Count { get; set; }
}

public partial class WidgetTallyProjection: MultiStreamProjection<WidgetTally, string>
{
    public WidgetTallyProjection()
    {
        Identity<WidgetMade>(_ => "tally");
    }

    public void Apply(WidgetMade _, WidgetTally tally) => tally.Count++;
}

#endregion

/// <summary>
/// jasperfx#502 — <c>IEventStore.GetProjectionStatusesAsync(tenantIdOrDatabaseIdentifier, ct)</c> used to open
/// its session against the default tenant no matter what was passed, so on a database-per-tenant store it
/// threw <c>DefaultTenantUsageDisabledException</c> — even when handed an identifier that
/// <c>AllDatabases()</c> had just returned and that <c>BuildProjectionDaemonAsync(identifier)</c> accepts.
///
/// That call is the only store-agnostic way to ask "is this projection still registered?", so monitoring
/// tools (CritterWatch) had to suppress orphan detection entirely for these stores: without a registry every
/// live projection's progression row looks orphaned.
/// </summary>
[Collection("multi-tenancy")]
public class projection_statuses_per_database: IAsyncLifetime
{
    // The test assembly runs once per target framework, concurrently, against the same Postgres. Give each
    // run its own databases and schema so the two don't fight over DDL or over each other's event sequences.
    private static readonly string Suffix = $"_net{Environment.Version.Major}";
    private static readonly string TenantA = $"mt502_tenant_a{Suffix}";
    private static readonly string TenantB = $"mt502_tenant_b{Suffix}";
    private static readonly string MasterSchema = $"mt502_master{Suffix}";
    private static readonly string SchemaName = $"mt502{Suffix}";

    private IDocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await conn.DropSchemaAsync(MasterSchema);
        var tenantA = await CreateDatabaseIfNotExists(conn, TenantA);
        var tenantB = await CreateDatabaseIfNotExists(conn, TenantB);
        await conn.CloseAsync();

        // Each run starts from sequence 0 so the assertions below are exact.
        await DropSchemaAsync(tenantA);
        await DropSchemaAsync(tenantB);

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
            opts.Projections.Add<WidgetTallyProjection>(ProjectionLifecycle.Async);
        });

        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _store.Dispose();
        return Task.CompletedTask;
    }

    private async Task AppendAsync(string tenantId, int count)
    {
        await using var session = _store.LightweightSession(tenantId);
        for (var i = 0; i < count; i++)
        {
            session.Events.StartStream(Guid.NewGuid(), new WidgetMade($"{tenantId}-{i}"));
        }

        await session.SaveChangesAsync();
    }

    private async Task RunDaemonAsync(string tenantId)
    {
        var daemon = await ((IEventStore)_store).BuildProjectionDaemonAsync(tenantId);
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(TimeSpan.FromSeconds(30));
        await daemon.StopAllAsync();
    }

    [Fact]
    public async Task reads_each_databases_projection_statuses_without_a_default_tenant()
    {
        await AppendAsync("tenant-a", 5);
        await AppendAsync("tenant-b", 2);
        await RunDaemonAsync("tenant-a");
        await RunDaemonAsync("tenant-b");

        var store = (IEventStore)_store;

        // Every database the store reports can be asked for its statuses. This threw before the fix.
        foreach (var database in await store.AllDatabases())
        {
            var statuses = await store.GetProjectionStatusesAsync(database.Identifier, CancellationToken.None);
            statuses.ShouldNotBeEmpty();
        }

        var alpha = await store.GetProjectionStatusesAsync("tenant-a", CancellationToken.None);
        var beta = await store.GetProjectionStatusesAsync("tenant-b", CancellationToken.None);

        // Each database reports its OWN progression, read from its own database.
        SequenceFor(alpha).ShouldBe(5);
        SequenceFor(beta).ShouldBe(2);

        // And the head sequence is that database's, not some other's.
        HeadFor(alpha).ShouldBe(5);
        HeadFor(beta).ShouldBe(2);
    }

    [Fact]
    public async Task a_database_per_tenant_stores_shards_are_not_tenant_suffixed()
    {
        await AppendAsync("tenant-a", 3);
        await RunDaemonAsync("tenant-a");

        var statuses = await ((IEventStore)_store)
            .GetProjectionStatusesAsync("tenant-a", CancellationToken.None);

        // Database-per-tenant runs one daemon per database over the SAME shard identities. Suffixing them
        // with the tenant would name a shard that exists nowhere, and its progression lookup would miss.
        var shard = statuses.Single(x => x.ProjectionName == nameof(WidgetTally)).Shards.Single();
        shard.ShardName.ShouldBe("WidgetTally:All");
        shard.ProcessedSequence.ShouldBe(3);
    }

    private static long SequenceFor(System.Collections.Generic.IReadOnlyList<ProjectionStatus> statuses)
        => statuses.Single(x => x.ProjectionName == nameof(WidgetTally)).Shards.Single().ProcessedSequence;

    private static long HeadFor(System.Collections.Generic.IReadOnlyList<ProjectionStatus> statuses)
        => statuses.Single(x => x.ProjectionName == nameof(WidgetTally)).Shards.Single().EventStoreSequence;

    private static async Task<string> CreateDatabaseIfNotExists(NpgsqlConnection conn, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);
        if (!await conn.DatabaseExists(databaseName))
        {
            try
            {
                await new DatabaseSpecification().BuildDatabase(conn, databaseName);
            }
            catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.DuplicateDatabase
                                                  or PostgresErrorCodes.UniqueViolation)
            {
                // The test assembly runs once per target framework, concurrently; the check-then-create above
                // is not atomic, so the loser of that race sees the database it wanted already there.
            }
        }

        builder.Database = databaseName;
        return builder.ConnectionString;
    }

    private static async Task DropSchemaAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await conn.DropSchemaAsync(SchemaName);
        await conn.CloseAsync();
    }
}
