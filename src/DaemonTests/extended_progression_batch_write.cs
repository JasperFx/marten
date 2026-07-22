using System;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Storage;
using Shouldly;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;

namespace DaemonTests;

public record BatchTelemetryEvent();

public class BatchTelemetryStream { public Guid Id { get; set; } }

public partial class BatchTelemetryProjection: SingleStreamProjection<BatchTelemetryStream, Guid>
{
    public void Apply(BatchTelemetryEvent @event, BatchTelemetryStream projection) { }
}

public partial class OtherBatchTelemetryProjection: SingleStreamProjection<BatchTelemetryStream, Guid>
{
    public OtherBatchTelemetryProjection()
    {
        Name = "OtherBatchTelemetry";
    }

    public void Apply(BatchTelemetryEvent @event, BatchTelemetryStream projection) { }
}

// jasperfx#553 — the batched extended-progression write. The JasperFx.Events ExtendedProgressionWriter
// coalesces every shard's heartbeat on a database into one batch per flush interval; Marten's overload
// must land the whole batch in ONE round trip with the exact semantics of
// mt_mark_event_progression_extended: update-only telemetry decoration of existing progression rows,
// never INSERT, never touch last_seq_id / last_updated.
public class extended_progression_batch_write: DaemonContext
{
    public extended_progression_batch_write(ITestOutputHelper output): base(output)
    {
    }

    private async Task<(object? heartbeat, object? status, object? reason, object? node, object? seq)> readRowAsync(
        string shard)
    {
        await using var session = theStore.QuerySession();
        await using var reader = await session.Connection
            .CreateCommand(
                $"select heartbeat, agent_status, pause_reason, running_on_node, last_seq_id from {theStore.Events.DatabaseSchemaName}.mt_event_progression where name = :name")
            .With("name", shard)
            .ExecuteReaderAsync();

        if (!await reader.ReadAsync()) return (null, null, null, null, null);

        object? at(int i) => await0(reader.GetValue(i));
        static object? await0(object raw) => raw is DBNull ? null : raw;

        return (at(0), at(1), at(2), at(3), at(4));
    }

    private async Task<long> countRowsAsync()
    {
        await using var session = theStore.QuerySession();
        var raw = await session.Connection
            .CreateCommand($"select count(*) from {theStore.Events.DatabaseSchemaName}.mt_event_progression")
            .ExecuteScalarAsync();
        return Convert.ToInt64(raw);
    }

    // #5022: the original helper started a live daemon, waited for it to catch up, then
    // StopAllAsync()'d it before the tests asserted on the rows. Daemon shutdown itself emits a
    // "Stopped" extended-progression heartbeat ASYNCHRONOUSLY, which is not drained before the daemon
    // is considered stopped — so it races back in and clobbers the very rows these tests assert on
    // (the intermittent `should be "Paused" but was "Stopped"` failure), and can reach an
    // already-disposed shutdown SemaphoreSlim on the JasperFx.Events side. Nothing here actually needs
    // a running daemon: seed the committed progression rows directly via mt_mark_event_progression so
    // the ONLY writer left against these rows is the WriteExtendedProgressionAsync call under test.
    private async Task seedProgressionRowsAsync()
    {
        StoreOptions(x =>
        {
            x.Events.EnableExtendedProgressionTracking = true;
            x.Projections.Add(new BatchTelemetryProjection(), ProjectionLifecycle.Async);
            x.Projections.Add(new OtherBatchTelemetryProjection(), ProjectionLifecycle.Async);
        });

        // Build the event storage (mt_event_progression + the mt_mark_event_progression* functions)
        // without ever starting a daemon.
        var database = (MartenDatabase)theStore.Storage.Database;
        await database.EnsureStorageExistsAsync(typeof(IEvent));

        await using var session = theStore.LightweightSession();
        foreach (var shard in new[] { "BatchTelemetryStream:All", "OtherBatchTelemetry:All" })
        {
            session.QueueSqlCommand(
                $"select {theStore.Events.DatabaseSchemaName}.mt_mark_event_progression(?, ?)", shard, 10L);
        }

        await session.SaveChangesAsync();
    }

    private static ShardState telemetry(string shard, string status, string? reason = null, int? node = null)
    {
        return new ShardState(shard, 10)
        {
            Action = ShardAction.Updated,
            AgentStatus = status,
            PauseReason = reason,
            LastHeartbeat = DateTimeOffset.UtcNow,
            RunningOnNode = node
        };
    }

    [Fact]
    public async Task updates_every_existing_row_in_one_batch()
    {
        await seedProgressionRowsAsync();

        var database = (MartenDatabase)theStore.Storage.Database;

        await database.WriteExtendedProgressionAsync([
            telemetry("BatchTelemetryStream:All", "Running", node: 3),
            telemetry("OtherBatchTelemetry:All", "Paused", "boom", node: 7)
        ]);

        var first = await readRowAsync("BatchTelemetryStream:All");
        first.status.ShouldBe("Running");
        first.heartbeat.ShouldNotBeNull();
        first.reason.ShouldBeNull();
        Convert.ToInt32(first.node).ShouldBe(3);

        var second = await readRowAsync("OtherBatchTelemetry:All");
        second.status.ShouldBe("Paused");
        second.reason.ShouldBe("boom");
        Convert.ToInt32(second.node).ShouldBe(7);
    }

    [Fact]
    public async Task never_inserts_a_row_and_never_touches_progression()
    {
        await seedProgressionRowsAsync();

        var database = (MartenDatabase)theStore.Storage.Database;
        var rowsBefore = await countRowsAsync();

        await database.WriteExtendedProgressionAsync([
            telemetry("BatchTelemetryStream:All", "Running"),
            // A shard that has never committed progression: no row to decorate, must be skipped
            // silently, exactly like the single-state function
            telemetry("NoSuchProjection:All:98123456", "Running")
        ]);

        var updated = await readRowAsync("BatchTelemetryStream:All");
        updated.status.ShouldBe("Running");
        Convert.ToInt64(updated.seq).ShouldBe(10); // committed progress untouched

        (await countRowsAsync()).ShouldBe(rowsBefore); // and nothing was inserted
        var missing = await readRowAsync("NoSuchProjection:All:98123456");
        missing.status.ShouldBeNull();
    }

    [Fact]
    public async Task an_empty_batch_is_a_no_op_and_a_single_state_batch_delegates()
    {
        await seedProgressionRowsAsync();

        var database = (MartenDatabase)theStore.Storage.Database;

        await database.WriteExtendedProgressionAsync(Array.Empty<ShardState>());

        await database.WriteExtendedProgressionAsync([
            telemetry("BatchTelemetryStream:All", "Stopped")
        ]);

        var row = await readRowAsync("BatchTelemetryStream:All");
        row.status.ShouldBe("Stopped");
    }
}
