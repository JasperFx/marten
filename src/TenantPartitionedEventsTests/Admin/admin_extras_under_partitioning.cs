using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Admin;

/// <summary>
/// #4617 section 3d / 3e — admin-API correctness pins that don't fit the
/// shared fixture (each needs a fresh schema or store-config that would
/// disturb sibling tests). Bundles three checklist items:
///
/// <list type="bullet">
///   <item>3d — <c>AddMartenManagedTenantsAsync(Guid[])</c> uses the
///     hyphen-free <c>N</c> format for partition suffixes (#4567).</item>
///   <item>3e — <c>AssertDatabaseMatchesConfigurationAsync</c> reports no
///     drift after a clean apply (the skipped FK + per-tenant sequence count
///     don't false-positive).</item>
///   <item>3e — <c>PerTenantEventSequences</c> schema object: exactly N
///     sequences after registering N tenants; re-apply is idempotent.</item>
/// </list>
/// </summary>
public class admin_extras_under_partitioning : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_xtra_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(_schema); } catch { }

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AddMartenManagedTenantsAsync_with_Guids_uses_hyphenfree_N_format_for_sequence_suffix()
    {
        // #4567: Guid-overload uses the lowercase 32-char hyphen-free "N"
        // format for the partition suffix — keeps the resulting sequence
        // name (mt_events_sequence_{N-formatted-guid} = 19 + 32 = 51 chars)
        // under Postgres' 63-byte identifier limit. The hyphenated "D" form
        // would also fit (51 chars too) but the spec pins N as the canonical
        // choice; pin it so a future refactor doesn't silently switch.
        //
        // Pinning ONLY the sequence name format here — the partition-table
        // naming uses Marten's internal `MartenManagedTenantListPartitions.NamedPartition`
        // shape which derives from the registered partition, not directly from
        // the Guid string — so a separate assertion would be coupling to
        // internals beyond the issue spec's concern.
        var tenantGuid = Guid.NewGuid();
        var expectedSuffix = tenantGuid.ToString("N"); // 32 hex chars, no hyphens

        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenantGuid);

        var sequenceExists = await SequenceExistsAsync(_schema, $"mt_events_sequence_{expectedSuffix}");
        sequenceExists.ShouldBeTrue(
            $"per-tenant sequence must be named mt_events_sequence_{expectedSuffix} (N-format) — " +
            "this pins the hyphen-free format choice from #4567");

        // Sanity: the hyphenated "D" form is NOT used.
        var hyphenatedShouldNotExist = await SequenceExistsAsync(_schema,
            $"mt_events_sequence_{tenantGuid.ToString("D")}");
        hyphenatedShouldNotExist.ShouldBeFalse(
            "the hyphenated D format must NOT be used — N is the canonical choice");
    }

    [Fact]
    public async Task AssertDatabaseMatchesConfigurationAsync_reports_no_drift_after_clean_apply()
    {
        // Pin: a clean apply under partitioning followed by an immediate
        // drift check finds no drift. The skipped explicit mt_events→mt_streams
        // FK (#4606) and the per-tenant sequence count must NOT register as
        // drift items — those are intentional shape choices, not missing
        // schema objects.
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        // After EnsureStorageExistsAsync (in InitializeAsync) + tenant
        // registration, the on-disk schema and the configuration should be
        // byte-identical. AssertDatabaseMatchesConfigurationAsync throws when
        // they diverge; Should.NotThrowAsync pins the no-drift contract.
        await Should.NotThrowAsync(async () =>
            await _store.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
    }

    [Fact]
    public async Task PerTenantEventSequences_exactly_one_sequence_per_registered_tenant()
    {
        // Pin: after registering N tenants, the schema has exactly N
        // mt_events_sequence_{suffix} sequences (NOT counting the
        // store-global mt_events_sequence). Re-applying the schema (via
        // EnsureStorageExistsAsync again) is idempotent — no new sequences,
        // no duplicates.
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "one", "two", "three");

        var sequencesAfterRegistration = await CountTenantSequencesAsync(_schema);
        sequencesAfterRegistration.ShouldBe(3L);

        // Idempotency: re-applying changes shouldn't change the count.
        await _store.Storage.Database.ApplyAllConfiguredChangesToDatabaseAsync();

        var sequencesAfterReapply = await CountTenantSequencesAsync(_schema);
        sequencesAfterReapply.ShouldBe(3L,
            "PerTenantEventSequences emits CREATE SEQUENCE IF NOT EXISTS — re-apply must be idempotent, no duplicates");
    }

    // ----- helpers -----

    private static async Task<bool> SequenceExistsAsync(string schema, string sequenceName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand(
            "select count(*) from pg_sequences where schemaname = @s and sequencename = @n");
        cmd.Parameters.AddWithValue("s", schema);
        cmd.Parameters.AddWithValue("n", sequenceName);
        return (long)(await cmd.ExecuteScalarAsync())! == 1L;
    }

    private static async Task<bool> TableExistsAsync(string schema, string tableName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand(
            "select count(*) from information_schema.tables where table_schema = @s and table_name = @t");
        cmd.Parameters.AddWithValue("s", schema);
        cmd.Parameters.AddWithValue("t", tableName);
        return (long)(await cmd.ExecuteScalarAsync())! == 1L;
    }

    private static async Task<long> CountTenantSequencesAsync(string schema)
    {
        // Count only the per-tenant sequences — exclude the store-global
        // mt_events_sequence (which always exists under partitioning, just
        // never gets nextval'd).
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand(
            "select count(*) from pg_sequences where schemaname = @s and sequencename like 'mt_events_sequence_%'");
        cmd.Parameters.AddWithValue("s", schema);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
