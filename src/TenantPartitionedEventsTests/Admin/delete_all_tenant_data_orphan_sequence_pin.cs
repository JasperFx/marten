using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Admin;

/// <summary>
/// #4683 — was the orphan-sequence + orphan-progression pin from #4617 section 3e.
/// <c>DeleteAllTenantDataAsync</c> and <c>RemoveMartenManagedTenantsAsync</c> now route
/// the per-tenant cleanup through <see cref="Marten.Internal.PerTenantPartitionedCleanup"/>:
/// after the partition drop they also <c>DROP SEQUENCE IF EXISTS mt_events_sequence_{suffix}</c>
/// and <c>DELETE</c> any <c>mt_event_progression</c> rows whose
/// <see cref="ShardName.TryParse"/> tenant slot (or
/// <see cref="HighWaterShardIdentity.PerTenantPrefix"/> match) is the dropped tenant.
/// Store-global progression rows are intentionally left alone.
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
            // #4683 progression test wants an async projection so the rebuild populates
            // per-tenant mt_event_progression rows we can then assert on.
            opts.Projections.Add<DelCountProjection>(ProjectionLifecycle.Async);
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DeleteAllTenantDataAsync_drops_partitions_and_the_per_tenant_sequence()
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

        // #4683: per-tenant sequence is now dropped (was the orphan-leak pin).
        var seqStillExists = await SequenceExistsAsync(_schema, $"mt_events_sequence_{tenant}");
        seqStillExists.ShouldBeFalse(
            "DeleteAllTenantDataAsync now drops the per-tenant mt_events_sequence_<tenant> via " +
            "PerTenantPartitionedCleanup (#4683). Was previously pinned as the orphan leak.");
    }

    [Fact]
    public async Task RemoveMartenManagedTenantsAsync_drops_partitions_and_the_per_tenant_sequence()
    {
        // Companion to the DeleteAll test for the alternate "drop everything for this tenant"
        // path. RemoveMartenManagedTenantsAsync skips TenantDataCleaner entirely (it's the
        // explicit "I no longer need this tenant" route), so this confirms PerTenantPartitionedCleanup
        // is wired in on *both* paths -- not just the cleaner's.
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

        // #4683: sequence is dropped too (was the second orphan-leak pin).
        (await SequenceExistsAsync(_schema, $"mt_events_sequence_{tenant}")).ShouldBeFalse(
            "RemoveMartenManagedTenantsAsync now drops the per-tenant mt_events_sequence_<tenant> " +
            "via PerTenantPartitionedCleanup (#4683). Was previously pinned as the orphan leak.");
    }

    [Fact]
    public async Task DeleteAllTenantDataAsync_removes_per_tenant_progression_rows_and_preserves_store_global()
    {
        // Two tenants. We seed the progression rows directly via SQL rather than driving the
        // daemon -- the daemon's per-tenant fan-out depends on a registered async projection +
        // ForceAllMartenDaemonActivity catch-up, and we want to test PerTenantPartitionedCleanup
        // in isolation: given a set of progression rows in the table, the cleanup deletes
        // exactly the dropped tenant's per-tenant rows (across both the ShardName grammar and
        // the HighWaterShardIdentity grammar) and leaves store-global rows alone.
        var keep = "keepme";
        var drop = "dropme";
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, keep, drop);

        // Touch the events table so the cleaner's batched DELETEs find data to delete (the
        // partition drop itself is the load-bearing part of this test; the actual event count
        // is incidental).
        await using (var session = _store.LightweightSession(drop))
        {
            session.Events.StartStream(Guid.NewGuid(), new DelEvent("x"));
            await session.SaveChangesAsync();
        }

        // Seed the progression rows we want the cleanup to target. Names span the two grammars:
        //  * HighWaterMark:<tenant>   — Marten's per-tenant high-water (HighWaterShardIdentity)
        //  * {Name}:All:<tenant>      — ShardName.Compose for per-tenant projection catch-up
        //  * {Name}:V2:All:<tenant>   — versioned variant
        // Plus a store-global "HighWaterMark" row + a "SomeProjection:All" row that must
        // survive the drop.
        await SeedProgressionRowsAsync(_schema, new[]
        {
            // store-global -- must survive
            "HighWaterMark",
            "SomeProjection:All",
            // per-tenant for the keep tenant -- must survive
            $"HighWaterMark:{keep}",
            $"DelCountProjection:All:{keep}",
            $"VersionedProjection:V2:All:{keep}",
            // per-tenant for the drop tenant -- must be removed
            $"HighWaterMark:{drop}",
            $"DelCountProjection:All:{drop}",
            $"VersionedProjection:V2:All:{drop}",
        });

        var beforeNames = await ReadProgressionRowNamesAsync(_schema);
        beforeNames.ShouldContain($"HighWaterMark:{drop}");
        beforeNames.ShouldContain($"DelCountProjection:All:{drop}");
        beforeNames.ShouldContain($"VersionedProjection:V2:All:{drop}");

        // Act.
        await _store.Advanced.DeleteAllTenantDataAsync(drop, CancellationToken.None);

        var afterNames = await ReadProgressionRowNamesAsync(_schema);

        // The dropped tenant's per-tenant rows are gone, across both grammars + the versioned form.
        afterNames.Any(n => MentionsTenant(n, drop)).ShouldBeFalse(
            $"Expected no progression rows mentioning '{drop}' after the drop, found: " +
            string.Join(", ", afterNames.Where(n => MentionsTenant(n, drop))));

        // The other tenant's per-tenant rows are untouched (also across both grammars).
        afterNames.ShouldContain($"HighWaterMark:{keep}");
        afterNames.ShouldContain($"DelCountProjection:All:{keep}");
        afterNames.ShouldContain($"VersionedProjection:V2:All:{keep}");

        // The store-global rows (no tenant suffix) are intentionally preserved.
        afterNames.ShouldContain(HighWaterShardIdentity.StoreGlobal,
            "the store-global HighWaterMark progression row must not be deleted by per-tenant cleanup");
        afterNames.ShouldContain("SomeProjection:All",
            "store-global projection catch-up rows must not be deleted by per-tenant cleanup");
    }

    private static async Task SeedProgressionRowsAsync(string schema, string[] names)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        foreach (var name in names)
        {
            await using var cmd = conn.CreateCommand(
                $"insert into \"{schema}\".\"mt_event_progression\" (name, last_seq_id) values (@n, 0) on conflict (name) do nothing");
            cmd.Parameters.AddWithValue("n", name);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ----- helpers -----

    /// <summary>Recognises both the per-tenant HighWaterMark grammar ("HighWaterMark:&lt;tenant&gt;")
    /// and the canonical ShardName grammar ({Name}:{ShardKey}:{tenant} / {Name}:V{n}:{ShardKey}:{tenant}).</summary>
    private static bool MentionsTenant(string progressionName, string tenantId)
    {
        if (progressionName.StartsWith(HighWaterShardIdentity.PerTenantPrefix, StringComparison.Ordinal))
        {
            return progressionName.Substring(HighWaterShardIdentity.PerTenantPrefix.Length) == tenantId;
        }
        return ShardName.TryParse(progressionName, out var shard)
            && shard?.TenantId == tenantId;
    }

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

    private static async Task<List<string>> ReadProgressionRowNamesAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand($"select name from \"{schema}\".\"mt_event_progression\"");
        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }
}

public record DelEvent(string Label);

public class DelCount
{
    public Guid Id { get; set; }
    public int Total { get; set; }
}

public partial class DelCountProjection: Marten.Events.Aggregation.SingleStreamProjection<DelCount, Guid>
{
    public DelCount Create(DelEvent e) => new() { Total = 1 };
    public void Apply(DelEvent e, DelCount agg) => agg.Total++;
}
