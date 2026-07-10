#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Xunit.Abstractions;

namespace CoreTests.Partitioning;

public record Bug4924WidgetMade(string Name);

/// <summary>
/// #4924 — under <c>UseTenantPartitionedEvents</c>, a tenant whose partition suffix contains a character
/// that is illegal in an <em>unquoted</em> Postgres identifier (most commonly '-') made schema apply throw.
///
/// <para>
/// Weasel stores the partition suffix as the tenant id verbatim
/// (<c>ManagedListPartitions.AddPartitionToAllTables</c>: <c>suffix.IsEmpty() ? value.ToLowerInvariant() : suffix</c>)
/// and sanitizes only the partition <em>table</em> name (<c>ListPartition.SanitizeSuffix</c>). Marten's
/// <c>PerTenantEventSequences.WriteCreateStatement</c> then interpolated that raw suffix into an unquoted
/// identifier, because Weasel's <c>SchemaUtils.QuoteName</c> quotes only reserved keywords and mixed-case
/// names:
/// </para>
/// <code>CREATE SEQUENCE IF NOT EXISTS my_schema.mt_events_sequence_tenant-a;</code>
/// <para>→ <c>42601: syntax error at or near "-"</c>, failing "All Configured Changes" for that database.</para>
///
/// <para>
/// <b>Quote, do not sanitize.</b> Every other path that names this sequence already quotes it and keeps the
/// raw suffix — <c>QuickAppendEventFunction</c> resolves it as
/// <c>format('%I.%I', schema, 'mt_events_sequence_' || partition_suffix)</c> straight from the tenants
/// table, and <c>EnsureSequencesAsync</c> / <c>PerTenantPartitionedCleanup</c> quote explicitly. Normalizing
/// the name at create time would produce a sequence the append function could never find, trading the
/// schema-apply error for <c>42P01 relation does not exist</c> on the first append. So the fix quotes the
/// two schema-apply statements, matching the rest of the codebase.
/// </para>
///
/// <para>
/// This is orthogonal to the #4567 assertion on <c>AddMartenManagedTenantsAsync</c>, which rejects unsafe
/// <em>suffixes</em> on the single-database path (use the <c>Guid[]</c> overload, which maps to the
/// hyphen-free "N" form). Sharded tenancy has always accepted hyphenated tenant ids — its own tests use
/// <c>tenant-a</c> — and derives the suffix from the tenant id verbatim. Such tenants were fine for document
/// partitioning, and only broke once per-tenant event sequences entered the picture.
/// </para>
/// </summary>
public class Bug_4924_hyphenated_tenant_id_sequence_name: IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _schema = "bug4924_p" + Environment.ProcessId;
    private readonly string _partitionSchema = "bug4924_tenants_p" + Environment.ProcessId;

    public Bug_4924_hyphenated_tenant_id_sequence_name(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(_partitionSchema); } catch { }
        try { await conn.DropSchemaAsync(_schema); } catch { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DocumentStore BuildStore() => (DocumentStore)DocumentStore.For(opts =>
    {
        opts.Connection(ConnectionSource.ConnectionString);
        opts.DatabaseSchemaName = _schema;
        opts.DisableNpgsqlLogging = true;

        opts.Policies.AllDocumentsAreMultiTenanted();
        opts.Policies.PartitionMultiTenantedDocumentsUsingMartenManagement(_partitionSchema);

        opts.Events.TenancyStyle = TenancyStyle.Conjoined;
        opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
        opts.Events.UseTenantPartitionedEvents = true;
        // Activate the events feature so schema apply actually emits the event tables and the per-tenant
        // sequences. Without a registered event type the feature is inactive and these tests are vacuous.
        opts.Events.AddEventType(typeof(Bug4924WidgetMade));

        opts.AutoCreateSchemaObjects = AutoCreate.All;
    });

    /// <summary>
    /// A database carrying a hyphenated <c>partition_suffix</c> — which sharded tenancy produces for a
    /// <c>tenant-a</c>, and which no API prevents — must still be migratable. Schema apply re-emits every
    /// registered tenant's CREATE SEQUENCE on each run, so an unquoted identifier wedged that database
    /// permanently: 42601, on every subsequent apply, for every store sharing the schema.
    /// </summary>
    [Theory]
    [InlineData("tenant-a")]
    [InlineData("6f1a3c2e-5b1d-4f0a-9c3e-8d7b6a5f4e3c")] // the GUID shape, which is all hyphens
    public async Task schema_apply_handles_a_partition_suffix_that_needs_quoting(string tenantId)
    {
        await using var store = BuildStore();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await RegisterRawSuffixAsync(tenantId);

        await using var reopened = BuildStore();
        var partitions = reopened.Options.TenantPartitions!.Partitions;
        await partitions.InitializeAsync((PostgresqlDatabase)reopened.Storage.Database, CancellationToken.None);
        partitions.Partitions.ShouldContainKey(tenantId);

        await Should.NotThrowAsync(() => reopened.Storage.ApplyAllConfiguredChangesToDatabaseAsync());

        // Created under the RAW name — anything else and the quick-append function's format('%I') lookup,
        // which reads partition_suffix straight from the tenants table, would miss it.
        (await SequenceCountAsync($"mt_events_sequence_{tenantId}")).ShouldBe(1L);
        _output.WriteLine($"schema apply created \"{_schema}\".\"mt_events_sequence_{tenantId}\"");
    }

    /// <summary>
    /// Re-applying schema over a live hyphenated tenant must stay idempotent. The additive-only delta means
    /// existing sequences are never dropped and recreated — doing so would reset a live tenant's seq_id and
    /// silently corrupt its event store.
    /// </summary>
    [Fact]
    public async Task re_applying_schema_does_not_reset_a_hyphenated_tenants_sequence()
    {
        const string tenantId = "tenant-a";

        await using var store = BuildStore();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await RegisterRawSuffixAsync(tenantId);

        await using var reopened = BuildStore();
        var partitions = reopened.Options.TenantPartitions!.Partitions;
        await partitions.InitializeAsync((PostgresqlDatabase)reopened.Storage.Database, CancellationToken.None);
        await reopened.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await AdvanceSequenceAsync(tenantId);
        var before = await LastValueAsync(tenantId);

        await reopened.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var after = await LastValueAsync(tenantId);

        _output.WriteLine($"sequence last_value before={before} after={after}");
        after.ShouldBe(before, "re-applying schema must not recreate (and reset) a live tenant's sequence");
    }

    /// <summary>Register the tenant the way sharded tenancy does: the raw tenant id as the partition suffix.</summary>
    private async Task RegisterRawSuffixAsync(string tenantId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var insert = conn.CreateCommand();
        insert.CommandText =
            $"insert into {_partitionSchema}.mt_tenant_partitions (partition_value, partition_suffix) " +
            "values (@v, @s) on conflict do nothing";
        insert.Parameters.AddWithValue("v", tenantId);
        insert.Parameters.AddWithValue("s", tenantId);
        await insert.ExecuteNonQueryAsync();
    }

    private async Task<long> SequenceCountAsync(string sequenceName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select count(*) from information_schema.sequences where sequence_schema = @s and sequence_name = @n";
        cmd.Parameters.AddWithValue("s", _schema);
        cmd.Parameters.AddWithValue("n", sequenceName);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task AdvanceSequenceAsync(string tenantId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select nextval('\"{_schema}\".\"mt_events_sequence_{tenantId}\"')";
        await cmd.ExecuteScalarAsync();
    }

    private async Task<long> LastValueAsync(string tenantId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select last_value from \"{_schema}\".\"mt_events_sequence_{tenantId}\"";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
