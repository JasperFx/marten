using System;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Xunit;

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
// must land the whole batch on ONE rented connection with the exact semantics of
// mt_mark_event_progression_extended: update-only telemetry decoration of existing progression rows,
// never INSERT, never touch last_seq_id / last_updated.
//
// #5167 — and it must land as one single-row statement per shard, each its own implicit transaction.
// The batch amortizes the CONNECTION, not the transaction: a multi-row statement holds a row lock on
// every shard in the batch until it commits, so one slow projection batch on one row stalls every other
// shard's telemetry (and whatever queues behind that) on the whole database.
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
        ], TestContext.Current.CancellationToken);

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
        ], TestContext.Current.CancellationToken);

        var updated = await readRowAsync("BatchTelemetryStream:All");
        updated.status.ShouldBe("Running");
        Convert.ToInt64(updated.seq).ShouldBe(10); // committed progress untouched

        (await countRowsAsync()).ShouldBe(rowsBefore); // and nothing was inserted
        var missing = await readRowAsync("NoSuchProjection:All:98123456");
        missing.status.ShouldBeNull();
    }

    // #5167 — the lock-convoy regression. A row that is locked by an in-flight projection batch is the
    // normal case, not an exotic one, and the batch write is going to wait on it either way. What must
    // NOT happen is the batch dragging every OTHER shard's row into that wait: measured against
    // PostgreSQL, an unrelated shard's progress write timed out after 4s queued behind a telemetry
    // statement that had locked its row on the way to a different, genuinely contended one.
    //
    // The rows are written in shard-name order, so "BatchTelemetryStream:All" goes first and the write
    // then parks on the deliberately-locked "OtherBatchTelemetry:All". Seeing the first row's telemetry
    // from ANOTHER connection while the batch is still parked is proof that its write committed on its
    // own — under a single multi-row statement nothing would be visible until the whole batch committed,
    // and the row would still be locked.
    [Fact]
    public async Task a_contended_row_does_not_hold_the_locks_of_the_rows_already_written()
    {
        await seedProgressionRowsAsync();

        var database = (MartenDatabase)theStore.Storage.Database;
        var schema = theStore.Events.DatabaseSchemaName;

        await using var blocker = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await blocker.OpenAsync(TestContext.Current.CancellationToken);
        await using var blocking = await blocker.BeginTransactionAsync(TestContext.Current.CancellationToken);

        // Stands in for a projection batch transaction sitting on its own progression row
        await blocker
            .CreateCommand(
                $"update {schema}.mt_event_progression set last_seq_id = last_seq_id where name = 'OtherBatchTelemetry:All'")
            .ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        var write = database.WriteExtendedProgressionAsync([
            telemetry("BatchTelemetryStream:All", "Running", node: 3),
            telemetry("OtherBatchTelemetry:All", "Paused", "boom")
        ], TestContext.Current.CancellationToken);

        // The first row's write commits on its own while the batch is parked on the contended one
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while ((await readRowAsync("BatchTelemetryStream:All")).status == null)
        {
            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline,
                "The first row's telemetry never became visible -- the batch is holding its lock");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        // ...and it is genuinely still parked, so that visibility was not just "the batch finished"
        write.IsCompleted.ShouldBeFalse();

        // The counterfactual from the report: an unrelated writer touching the already-written row must
        // not queue behind the batch. lock_timeout makes "it waited" a failure instead of a hang.
        await using (var unrelated = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await unrelated.OpenAsync(TestContext.Current.CancellationToken);
            await unrelated.CreateCommand($"""
                set lock_timeout = '2s';
                update {schema}.mt_event_progression set last_seq_id = last_seq_id
                where name = 'BatchTelemetryStream:All';
                """).ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await blocking.RollbackAsync(TestContext.Current.CancellationToken);

        await write;
        (await readRowAsync("OtherBatchTelemetry:All")).status.ShouldBe("Paused");
    }

    // #5167 Finding 3 — the SET list used to be unconditional, so every flush gave every matched row a
    // new tuple version whether anything had changed or not, on a small hot table. xmin is the
    // inserting transaction of the live tuple, so an unchanged xmin is exactly "this row was not
    // rewritten".
    [Fact]
    public async Task replaying_identical_telemetry_does_not_rewrite_the_row()
    {
        await seedProgressionRowsAsync();

        var database = (MartenDatabase)theStore.Storage.Database;
        var state = telemetry("BatchTelemetryStream:All", "Running", node: 3);

        await database.WriteExtendedProgressionAsync([state], TestContext.Current.CancellationToken);
        var written = await readTupleVersionAsync("BatchTelemetryStream:All");

        // Byte-identical replay: nothing to change, so nothing is written
        await database.WriteExtendedProgressionAsync([state], TestContext.Current.CancellationToken);
        (await readTupleVersionAsync("BatchTelemetryStream:All")).ShouldBe(written);

        // ...but a real change still lands
        await database.WriteExtendedProgressionAsync([
            telemetry("BatchTelemetryStream:All", "Paused", "boom", node: 3)
        ], TestContext.Current.CancellationToken);

        (await readTupleVersionAsync("BatchTelemetryStream:All")).ShouldNotBe(written);
        (await readRowAsync("BatchTelemetryStream:All")).status.ShouldBe("Paused");
    }

    private async Task<string> readTupleVersionAsync(string shard)
    {
        await using var session = theStore.QuerySession();
        var raw = await session.Connection
            .CreateCommand(
                $"select xmin::text from {theStore.Events.DatabaseSchemaName}.mt_event_progression where name = :name")
            .With("name", shard)
            .ExecuteScalarAsync();

        return (string)raw!;
    }

    [Fact]
    public async Task an_empty_batch_is_a_no_op_and_a_single_state_batch_delegates()
    {
        await seedProgressionRowsAsync();

        var database = (MartenDatabase)theStore.Storage.Database;

        await database.WriteExtendedProgressionAsync(Array.Empty<ShardState>(), TestContext.Current.CancellationToken);

        await database.WriteExtendedProgressionAsync([
            telemetry("BatchTelemetryStream:All", "Stopped")
        ], TestContext.Current.CancellationToken);

        var row = await readRowAsync("BatchTelemetryStream:All");
        row.status.ShouldBe("Stopped");
    }
}
