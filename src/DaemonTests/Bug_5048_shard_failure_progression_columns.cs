using System;
using System.Linq;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx.Core;
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

public record FailureTelemetryEvent();

/// <summary>
/// Deserializes only while <see cref="DeserializationFails" /> is off. Both serializers run the
/// parameterless constructor, so flipping the flag corrupts every persisted body of this type on the
/// READ side without touching the rows -- which is precisely the shape of the failure #5048 is about.
/// The flag is private to this test class (whose facts never run concurrently with each other) rather
/// than reusing the process-global FailingEvent.SerializationFails.
/// </summary>
public class CorruptibleEvent
{
    public static bool DeserializationFails;

    public CorruptibleEvent()
    {
        if (DeserializationFails) throw new DivideByZeroException("Boom!");
    }
}

public class FailureTelemetryStream { public Guid Id { get; set; } }

public partial class FailureTelemetryProjection: SingleStreamProjection<FailureTelemetryStream, Guid>
{
    public void Apply(FailureTelemetryEvent @event, FailureTelemetryStream projection) { }

    // The shard only LOADS event types it is interested in, so the corruptible type has to be part of
    // the projection for the read to ever reach it.
    public void Apply(CorruptibleEvent @event, FailureTelemetryStream projection) { }
}

// #5048 / jasperfx#565. ShardState.Failure now rides along on the paused/stopped states the daemon
// publishes; these tests pin the persistence half. A supervisor polling the database -- CritterWatch
// when the publishing node is DOWN, which is exactly when this matters -- must see the same classified
// reason an in-process observer does, and must stop seeing it once the shard recovers.
//
// Follows the #5022 pattern from extended_progression_batch_write: seed the committed progression rows
// directly instead of running a daemon, so the ONLY writer against these rows is the call under test.
public class Bug_5048_shard_failure_progression_columns: DaemonContext
{
    private const string TheShard = "FailureTelemetryStream:All";

    public Bug_5048_shard_failure_progression_columns(ITestOutputHelper output): base(output)
    {
    }

    private async Task seedProgressionRowAsync()
    {
        StoreOptions(x =>
        {
            x.Events.EnableExtendedProgressionTracking = true;
            x.Projections.Add(new FailureTelemetryProjection(), ProjectionLifecycle.Async);
        });

        var database = (MartenDatabase)theStore.Storage.Database;
        await database.EnsureStorageExistsAsync(typeof(IEvent));

        await using var session = theStore.LightweightSession();
        session.QueueSqlCommand(
            $"select {theStore.Events.DatabaseSchemaName}.mt_mark_event_progression(?, ?)", TheShard, 10L);
        await session.SaveChangesAsync();
    }

    private async Task<(object? category, object? sequence, object? eventType, object? tenantId)> readFailureAsync()
    {
        await using var session = theStore.QuerySession();
        await using var reader = await session.Connection
            .CreateCommand(
                $"select failure_category, failure_event_sequence, failure_event_type, failure_event_tenant_id from {theStore.Events.DatabaseSchemaName}.mt_event_progression where name = :name")
            .With("name", TheShard)
            .ExecuteReaderAsync();

        if (!await reader.ReadAsync()) return (null, null, null, null);

        object? at(int i) => reader.GetValue(i) is DBNull ? null : reader.GetValue(i);
        return (at(0), at(1), at(2), at(3));
    }

    private static ShardState paused(ShardFailureCategory category, long sequence, string eventTypeName,
        string? tenantId = null)
    {
        var failure = new ShardFailure
        {
            Category = category,
            ExceptionType = "Marten.Exceptions.EventDeserializationFailureException",
            RootExceptionType = "System.DivideByZeroException",
            Message = "Boom!",
            Detail = "Marten.Exceptions.EventDeserializationFailureException: Boom!\n   at Somewhere",
            OccurredAt = DateTimeOffset.UtcNow,
            Event = new EventFailureDetails
            {
                Sequence = sequence, EventTypeName = eventTypeName, TenantId = tenantId
            }
        };

        return new ShardState(TheShard, 10)
        {
            Action = ShardAction.Paused,
            AgentStatus = "Paused",
            PauseReason = failure.Detail,
            Failure = failure,
            LastHeartbeat = DateTimeOffset.UtcNow
        };
    }

    private static ShardState withoutFailure(ShardAction action, string status)
    {
        return new ShardState(TheShard, 10)
        {
            Action = action, AgentStatus = status, LastHeartbeat = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public async Task persists_the_classified_failure_on_a_paused_shard()
    {
        await seedProgressionRowAsync();
        var database = (MartenDatabase)theStore.Storage.Database;

        await database.WriteExtendedProgressionAsync(
            paused(ShardFailureCategory.EventSerialization, 4815, "failure_telemetry_event", "tenant-a"));

        var row = await readFailureAsync();

        // The enum NAME, never the ordinal -- reordering ShardFailureCategory must not silently re-label
        // rows that were written by an older deployment.
        row.category.ShouldBe(nameof(ShardFailureCategory.EventSerialization));
        Convert.ToInt64(row.sequence).ShouldBe(4815);
        row.eventType.ShouldBe("failure_telemetry_event");
        row.tenantId.ShouldBe("tenant-a");
    }

    [Fact]
    public async Task a_recovered_shard_stops_reporting_the_reason_it_paused()
    {
        await seedProgressionRowAsync();
        var database = (MartenDatabase)theStore.Storage.Database;

        await database.WriteExtendedProgressionAsync(
            paused(ShardFailureCategory.ApplyEvent, 99, "failure_telemetry_event"));
        (await readFailureAsync()).category.ShouldNotBeNull();

        // A restart supersedes whatever paused the agent last. Without this, every supervisor built on
        // these columns alerts forever on a failure the operator already fixed.
        await database.WriteExtendedProgressionAsync(withoutFailure(ShardAction.Started, "Running"));

        var row = await readFailureAsync();
        row.category.ShouldBeNull();
        row.sequence.ShouldBeNull();
        row.eventType.ShouldBeNull();
        row.tenantId.ShouldBeNull();
    }

    [Fact]
    public async Task a_failureless_non_start_publication_leaves_the_reason_alone()
    {
        await seedProgressionRowAsync();
        var database = (MartenDatabase)theStore.Storage.Database;

        await database.WriteExtendedProgressionAsync(
            paused(ShardFailureCategory.EventSerialization, 4815, "failure_telemetry_event"));

        // This is the load-bearing case: SubscriptionAgent publishes a plain Stopped state (no Failure)
        // right behind the Paused one, and a heartbeat can arrive with no failure at all. An
        // unconditional write would erase the reason microseconds after recording it.
        await database.WriteExtendedProgressionAsync(withoutFailure(ShardAction.Stopped, "Stopped"));
        await database.WriteExtendedProgressionAsync(withoutFailure(ShardAction.Updated, "Running"));

        var row = await readFailureAsync();
        row.category.ShouldBe(nameof(ShardFailureCategory.EventSerialization));
        Convert.ToInt64(row.sequence).ShouldBe(4815);
    }

    [Fact]
    public async Task rehydrates_the_failure_on_the_read_side()
    {
        await seedProgressionRowAsync();
        var database = (MartenDatabase)theStore.Storage.Database;

        await database.WriteExtendedProgressionAsync(
            paused(ShardFailureCategory.UnknownEventType, 1623, "trip_started", "tenant-b"));

        // A poller must get the same shape as a live ShardState observer, not just "it's Paused"
        var states = await database.AllProjectionProgress();
        var state = states.Single(x => x.ShardName == TheShard);

        state.Failure.ShouldNotBeNull();
        state.Failure.Category.ShouldBe(ShardFailureCategory.UnknownEventType);
        state.Failure.Event.ShouldNotBeNull();
        state.Failure.Event.Sequence.ShouldBe(1623);
        state.Failure.Event.EventTypeName.ShouldBe("trip_started");
        state.Failure.Event.TenantId.ShouldBe("tenant-b");

        // ShardFailure.Detail is exactly what PauseReason has always carried, which is why the reason
        // text needed no column of its own.
        state.Failure.Detail.ShouldBe(state.PauseReason);
    }

    [Fact]
    public async Task a_healthy_shard_reports_no_failure()
    {
        await seedProgressionRowAsync();
        var database = (MartenDatabase)theStore.Storage.Database;

        await database.WriteExtendedProgressionAsync(withoutFailure(ShardAction.Started, "Running"));

        var states = await database.AllProjectionProgress();
        states.Single(x => x.ShardName == TheShard).Failure.ShouldBeNull();
    }

    // The acceptance case the issue was written for: before this, a shard paused by a corrupted event
    // body classified as ShardFailureCategory.Other with no event details, because the daemon has no
    // fallback type-name sniffing -- Marten's exception has to declare its own category.
    [Fact]
    public async Task a_projection_paused_by_a_corrupted_event_body_reports_the_serialization_failure()
    {
        CorruptibleEvent.DeserializationFails = false;

        StoreOptions(x =>
        {
            x.Events.EnableExtendedProgressionTracking = true;
            x.Projections.Add(new FailureTelemetryProjection(), ProjectionLifecycle.Async);
            // Pause on a bad body instead of dead-lettering it and moving on
            x.Projections.Errors.SkipSerializationErrors = false;
        }, true);

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, new FailureTelemetryEvent(), new CorruptibleEvent());
            await session.SaveChangesAsync();
        }

        long corruptedSequence;
        await using (var session = theStore.QuerySession())
        {
            var events = await session.Events.FetchStreamAsync(streamId);
            corruptedSequence = events.Single(x => x.EventType == typeof(CorruptibleEvent)).Sequence;
        }

        var alias = theStore.Events.EventMappingFor<CorruptibleEvent>().EventTypeName;

        try
        {
            CorruptibleEvent.DeserializationFails = true;

            using var daemon = await StartDaemon();
            var waiter = daemon.Tracker.WaitForShardCondition(x => x.Failure != null,
                "the shard reports a classified failure", 30.Seconds());

            await daemon.StartAllAsync();

            var state = await waiter;

            state.Failure.ShouldNotBeNull();
            state.Failure.Category.ShouldBe(ShardFailureCategory.EventSerialization);
            state.Failure.Event.ShouldNotBeNull();
            state.Failure.Event.Sequence.ShouldBe(corruptedSequence);
            state.Failure.Event.EventTypeName.ShouldBe(alias);

            await daemon.StopAllAsync();
        }
        finally
        {
            CorruptibleEvent.DeserializationFails = false;
        }
    }

    [Fact]
    public async Task never_inserts_a_row_and_never_touches_committed_progression()
    {
        await seedProgressionRowAsync();
        var database = (MartenDatabase)theStore.Storage.Database;

        await database.WriteExtendedProgressionAsync([
            paused(ShardFailureCategory.ApplyEvent, 7, "failure_telemetry_event"),
            // A shard that has never committed progression has nowhere to record a reason: skipped
            // silently, exactly like every other extended-progression write.
            new ShardState("NoSuchProjection:All:98123456", 10)
            {
                Action = ShardAction.Paused, AgentStatus = "Paused", LastHeartbeat = DateTimeOffset.UtcNow
            }
        ]);

        await using var session = theStore.QuerySession();
        var rows = Convert.ToInt64(await session.Connection
            .CreateCommand($"select count(*) from {theStore.Events.DatabaseSchemaName}.mt_event_progression")
            .ExecuteScalarAsync());
        rows.ShouldBe(1);

        var sequence = Convert.ToInt64(await session.Connection
            .CreateCommand(
                $"select last_seq_id from {theStore.Events.DatabaseSchemaName}.mt_event_progression where name = :name")
            .With("name", TheShard)
            .ExecuteScalarAsync());
        sequence.ShouldBe(10); // committed progress untouched
    }
}
