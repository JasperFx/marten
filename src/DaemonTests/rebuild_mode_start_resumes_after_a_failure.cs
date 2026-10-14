using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using DaemonTests.TestingSupport;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace DaemonTests;

public record RestartableIncrement(int Index);

public class RestartableTally
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Throws while folding the event with index <see cref="FailOnIndex" />, for as long as <see cref="Failing" /> is on,
/// and counts every successful application per event index so a replay over already-built documents shows up.
/// The state is static and private to this one test class, whose facts never run concurrently with each other.
/// </summary>
public partial class RestartableTallyProjection: SingleStreamProjection<RestartableTally, Guid>
{
    public static volatile bool Failing;
    public static int FailOnIndex;
    public static readonly ConcurrentDictionary<int, int> Applications = new();

    public RestartableTallyProjection()
    {
        // Small pages, so several of them commit before the failing one
        Options.BatchSize = 10;
    }

    public void Apply(RestartableIncrement @event, RestartableTally tally)
    {
        if (Failing && @event.Index == FailOnIndex)
        {
            throw new DivideByZeroException($"Boom on event {@event.Index}");
        }

        tally.Count++;
        Applications.AddOrUpdate(@event.Index, 1, (_, count) => count + 1);
    }
}

/// <summary>
/// JasperFx/jasperfx#1015 (wolverine#4569): a projection shard started in Rebuild mode with
/// <see cref="IProjectionDaemon.StartAgentInRebuildModeAsync" /> that fails part of the way through can be stopped and
/// started again, and the second run finishes the rebuild from the progress the first run committed, instead of
/// replaying from the beginning over the documents the first run already built.
///
/// Uses extended progression tracking and nothing else: no schema change beyond the columns that feature already
/// has. Before #1015, every position strategy answered Rebuild with a floor of 0 and a rewind of the progression
/// row, so the second start deleted the row and folded every event again on top of the existing documents.
/// </summary>
public class rebuild_mode_start_resumes_after_a_failure: DaemonContext
{
    private const int NumberOfEvents = 100;
    private static readonly ShardName TheShard = new("RestartableTally");

    public rebuild_mode_start_resumes_after_a_failure(ITestOutputHelper output): base(output)
    {
        RestartableTallyProjection.Applications.Clear();
        RestartableTallyProjection.Failing = false;
        RestartableTallyProjection.FailOnIndex = 60;
    }

    private void configure()
    {
        StoreOptions(x =>
        {
            x.Events.EnableExtendedProgressionTracking = true;
            x.Projections.Add(new RestartableTallyProjection(), ProjectionLifecycle.Async);
        });
    }

    // One event per stream: a document that was folded twice has a Count of 2
    private async Task appendAsync(int from, int count)
    {
        await using var session = theStore.LightweightSession();
        for (var index = from; index < from + count; index++)
        {
            session.Events.StartStream<RestartableTally>(Guid.NewGuid(), new RestartableIncrement(index));
        }

        await session.SaveChangesAsync();
    }

    private async Task<(long lastSeqId, string? agentStatus, string? failureCategory)> readProgressionAsync()
    {
        await using var session = theStore.QuerySession();
        await using var reader = await session.Connection
            .CreateCommand(
                $"select last_seq_id, agent_status, failure_category from {theStore.Events.DatabaseSchemaName}.mt_event_progression where name = :name")
            .With("name", TheShard.Identity)
            .ExecuteReaderAsync();

        if (!await reader.ReadAsync()) return (0, null, null);

        string? text(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
        return (reader.GetInt64(0), text(1), text(2));
    }

    private async Task<long> highestSequenceAsync()
    {
        await using var session = theStore.QuerySession();
        return await session.Events.QueryAllRawEvents().MaxAsync(x => x.Sequence);
    }

    private async Task waitUntilAsync(Func<Task<bool>> condition, string description)
    {
        var deadline = DateTimeOffset.UtcNow.Add(30.Seconds());
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }

        throw new TimeoutException($"Timed out waiting until {description}");
    }

    [Fact]
    public async Task a_failed_rebuild_mode_run_can_be_restarted_and_finished_from_its_committed_progress()
    {
        configure();
        await theStore.Advanced.Clean.CompletelyRemoveAllAsync();
        await appendAsync(1, NumberOfEvents);

        using var daemon = await theStore.BuildProjectionDaemonAsync();

        // First run: fails on event 60
        RestartableTallyProjection.Failing = true;
        var paused = new ShardActionWatcher(daemon.Tracker, TheShard.Identity, ShardAction.Paused, 30.Seconds());

        var first = await daemon.StartAgentInRebuildModeAsync(TheShard, TestContext.Current.CancellationToken);
        first.Mode.ShouldBe(ShardExecutionMode.Rebuild);

        await paused.Task;

        // The progress the first run committed, and the failure, both recorded through extended progression tracking.
        // The extended columns are written by the progression writer on its own flush, a moment after the pause
        await waitUntilAsync(async () => (await readProgressionAsync()).failureCategory != null,
            "extended progression tracking records the failure");
        var afterFailure = await readProgressionAsync();
        afterFailure.lastSeqId.ShouldBeGreaterThan(0);
        afterFailure.lastSeqId.ShouldBeLessThan(await highestSequenceAsync());
        afterFailure.failureCategory.ShouldNotBeNull();

        // Every event at or below the committed mark is in a document already; nothing past the failing event is
        var committedIndexes = RestartableTallyProjection.Applications.Keys.Where(i => i < 60).ToArray();
        committedIndexes.ShouldNotBeEmpty();

        // Stop, clear the fault, and start the rebuild-mode run again
        await daemon.StopAgentAsync(TheShard);
        RestartableTallyProjection.Failing = false;

        var second = await daemon.StartAgentInRebuildModeAsync(TheShard, TestContext.Current.CancellationToken);
        second.Mode.ShouldBe(ShardExecutionMode.Rebuild);

        var highest = await highestSequenceAsync();
        await waitUntilAsync(async () => (await readProgressionAsync()).lastSeqId >= highest,
            $"the shard reaches the high-water mark {highest}");

        // Finished: every event folded into its document exactly once. A replay from the floor over the documents the
        // first run built would leave every committed stream with a Count of 2
        await using (var session = theStore.QuerySession())
        {
            var tallies = await session.Query<RestartableTally>().ToListAsync();
            tallies.Count.ShouldBe(NumberOfEvents);
            tallies.ShouldAllBe(x => x.Count == 1);
        }

        var afterRestart = await readProgressionAsync();
        afterRestart.failureCategory.ShouldBeNull();

        // No event below the first run's committed mark was folded again by the second run
        await using (var session = theStore.QuerySession())
        {
            var committedSequences = await session.Events.QueryAllRawEvents()
                .Where(x => x.Sequence <= afterFailure.lastSeqId)
                .ToListAsync();

            foreach (var e in committedSequences)
            {
                var index = ((RestartableIncrement)e.Data).Index;
                RestartableTallyProjection.Applications[index].ShouldBe(1, $"event {index} at sequence {e.Sequence}");
            }
        }

        // And the restarted run keeps following the store past where the rebuild started
        await appendAsync(NumberOfEvents + 1, 5);
        var newHighest = await highestSequenceAsync();
        await waitUntilAsync(async () => (await readProgressionAsync()).lastSeqId >= newHighest,
            $"the shard follows the store to {newHighest}");

        daemon.StatusFor(TheShard.Identity).ShouldBe(AgentStatus.Running);
    }
}
