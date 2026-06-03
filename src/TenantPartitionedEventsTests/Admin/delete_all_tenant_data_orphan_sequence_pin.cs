using System;
using System.Threading;
using System.Threading.Tasks;
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
/// #4617 section 3e — pin the orphan-sequence behavior on
/// <c>DeleteAllTenantDataAsync(tenantId)</c>. The cleaner drops the tenant's
/// partition tables (mt_events_&lt;tenant&gt; / mt_streams_&lt;tenant&gt;)
/// AND deletes rows from the parent mt_events / mt_streams tables, but it
/// does NOT drop the per-tenant <c>mt_events_sequence_&lt;tenant&gt;</c>
/// sequence (no equivalent cleanup hook on
/// <see cref="Marten.Internal.TenantDataCleaner"/>).
///
/// <para>
/// Consequence: re-registering the same tenant id after a
/// <c>DeleteAllTenantDataAsync</c> picks up the OLD sequence and the new
/// events' seq_ids continue from the last value, not from 1. Pinned so a
/// future fix that DOES drop the sequence flips the assertion intentionally.
/// </para>
///
/// <para>
/// Own-store because this test scrubs partition + sequence state in ways
/// the shared GuidPartitionedFixture's other tests can't tolerate.
/// </para>
/// </summary>
public class delete_all_tenant_data_orphan_sequence_pin : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_del_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

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

            opts.Events.AddEventType<DelEvent>();
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DeleteAllTenantDataAsync_drops_partitions_but_LEAVES_the_per_tenant_sequence()
    {
        var tenant = "delpin";
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        // Seed some events so the per-tenant sequence advances.
        var streamId = Guid.NewGuid();
        await using (var session = _store.LightweightSession(tenant))
        {
            session.Events.StartStream(streamId,
                new DelEvent("a"), new DelEvent("b"), new DelEvent("c"));
            await session.SaveChangesAsync();
        }

        var seqValueBefore = await ReadSequenceLastValueAsync(_schema, $"mt_events_sequence_{tenant}");
        seqValueBefore.ShouldBeGreaterThanOrEqualTo(3L, "the sequence advanced past the 3 appended events");

        // Act: delete all data for this tenant.
        await _store.Advanced.DeleteAllTenantDataAsync(tenant, CancellationToken.None);

        // The partition tables are gone — pinning the cleaner's positive effect.
        var partitionExists = await TableExistsAsync(_schema, $"mt_events_{tenant}");
        partitionExists.ShouldBeFalse(
            "DeleteAllTenantDataAsync drops the tenant's mt_events partition table");

        // The orphan-sequence pin: the per-tenant sequence is STILL THERE.
        var seqStillExists = await SequenceExistsAsync(_schema, $"mt_events_sequence_{tenant}");
        seqStillExists.ShouldBeTrue(
            "DeleteAllTenantDataAsync does NOT drop the per-tenant mt_events_sequence_<tenant> " +
            "— pinned as the documented leak. A future fix to TenantDataCleaner that DOES drop the " +
            "sequence will flip this assertion to ShouldBeFalse and force a contract review.");
    }

    [Fact]
    public async Task RemoveMartenManagedTenantsAsync_drops_partitions_but_LEAVES_the_per_tenant_sequence()
    {
        // Companion pin for RemoveMartenManagedTenantsAsync (the explicit
        // "I no longer need this tenant" path, vs DeleteAllTenantDataAsync's
        // "wipe this tenant's data but keep registration"). Same partition-drop
        // but no sequence drop — same orphan leak.
        var tenant = "rempin";
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        await using (var session = _store.LightweightSession(tenant))
        {
            session.Events.StartStream(streamId,
                new DelEvent("x"), new DelEvent("y"));
            await session.SaveChangesAsync();
        }

        var seqValueBefore = await ReadSequenceLastValueAsync(_schema, $"mt_events_sequence_{tenant}");
        seqValueBefore.ShouldBeGreaterThanOrEqualTo(2L);

        await _store.Advanced.RemoveMartenManagedTenantsAsync(new[] { tenant }, CancellationToken.None);

        // Partition table dropped.
        (await TableExistsAsync(_schema, $"mt_events_{tenant}")).ShouldBeFalse();

        // Sequence still present — the orphan-leak pin.
        (await SequenceExistsAsync(_schema, $"mt_events_sequence_{tenant}")).ShouldBeTrue(
            "RemoveMartenManagedTenantsAsync drops partitions but does NOT drop the per-tenant " +
            "mt_events_sequence_<tenant> — pinned as the documented leak");
    }

    // ----- helpers -----

    private static async Task<long> ReadSequenceLastValueAsync(string schema, string sequenceName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand(
            "select last_value from pg_sequences where schemaname = @s and sequencename = @n");
        cmd.Parameters.AddWithValue("s", schema);
        cmd.Parameters.AddWithValue("n", sequenceName);
        var raw = await cmd.ExecuteScalarAsync();
        return raw is long v ? v : (raw is null || raw == DBNull.Value ? 0L : Convert.ToInt64(raw));
    }

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
}

public record DelEvent(string Label);
