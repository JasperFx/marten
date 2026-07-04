using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events.Daemon.HighWater;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// The companion to <see cref="bulk_insert_advances_per_tenant_high_water"/>: the per-tenant high-water fix
/// (commit on JasperFx/marten#4806's branch) added a per-tenant branch to BulkEventAppender but must leave
/// the store-global behavior untouched for every non-partitioned store. This asserts that a plain store still
/// advances the single store-global 'HighWaterMark' row to max(seq_id) after a bulk insert, and writes no
/// 'HighWaterMark:&lt;tenant&gt;' row — so nothing regressed for the common case.
/// </summary>
public class bulk_insert_advances_store_global_high_water_on_non_partitioned_store
{
    public record Probe(int Value);

    [Fact]
    public async Task advances_the_store_global_row_and_writes_no_per_tenant_row()
    {
        var schema = $"tp_bulk_global_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 40);
        await using (var reset = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await reset.OpenAsync();
            try { await reset.DropSchemaAsync(schema); } catch (Exception) { }
        }

        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = schema;
            // UseTenantPartitionedEvents stays false — a single store-global high-water store.
            o.Events.AddEventType<Probe>();
        });

        var streamId = Guid.NewGuid();
        var action = StreamAction.Start(store.Events, streamId,
            new Probe(1), new Probe(2), new Probe(3));

        await store.BulkInsertEventsAsync(new[] { action });

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        long maxSeq;
        await using (var cmd = new NpgsqlCommand($"select max(seq_id) from {schema}.mt_events", conn))
        {
            maxSeq = (long)(await cmd.ExecuteScalarAsync())!;
        }

        maxSeq.ShouldBeGreaterThan(0);

        // The store-global row must be advanced to the store's height.
        await using (var cmd = new NpgsqlCommand(
                         $"select last_seq_id from {schema}.mt_event_progression where name = @name", conn))
        {
            cmd.Parameters.AddWithValue("name", HighWaterShardIdentity.StoreGlobal);
            var global = await cmd.ExecuteScalarAsync();
            global.ShouldNotBeNull("bulk insert on a non-partitioned store must advance the store-global HighWaterMark");
            ((long)global!).ShouldBe(maxSeq);
        }

        // And no per-tenant machinery should appear — every progression row is the store-global one.
        await using (var cmd = new NpgsqlCommand(
                         $"select count(*) from {schema}.mt_event_progression where name like 'HighWaterMark:%'", conn))
        {
            var perTenantRows = (long)(await cmd.ExecuteScalarAsync())!;
            perTenantRows.ShouldBe(0, "a non-partitioned store must not write any HighWaterMark:<tenant> row");
        }
    }
}
