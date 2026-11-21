using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.Progress;
using Marten.Storage;
using Marten.Subscriptions;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DaemonTests.Bugs;

public record EarlyPauseWeighed(bool Poison);

public class EarlyPauseWeighIn
{
    // Static because the daemon builds the aggregate itself. DaemonTests never run in parallel.
    public static bool PoisonFails = true;

    public Guid Id { get; set; }
    public int Count { get; set; }

    public void Apply(EarlyPauseWeighed e)
    {
        if (e.Poison && PoisonFails) throw new InvalidOperationException("Can't weigh this one");
        Count++;
    }
}

public class EarlyFailingSubscription: SubscriptionBase
{
    public EarlyFailingSubscription()
    {
        Name = "EarlyFailing";
    }

    public bool Fails { get; set; } = true;

    public List<long> Processed { get; } = new();

    public override Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
        IDocumentOperations operations, CancellationToken cancellationToken)
    {
        if (Fails) throw new InvalidOperationException("Not today");

        Processed.AddRange(page.Events.Select(x => x.Sequence));
        return Task.FromResult(NullChangeListener.Instance);
    }
}

/// <summary>
///     marten#6127: a shard that fails before its first commit has no progression row, so its failure was written
///     nowhere. It now gets a row at the position it started from, which leaves where it starts next time alone.
/// </summary>
public class Bug_6127_failure_before_the_first_commit: OneOffConfigurationsContext
{
    private const string TheShard = "EarlyPauseWeighIn:All";
    private static readonly ShardName TheShardName = new("EarlyPauseWeighIn");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public Bug_6127_failure_before_the_first_commit()
    {
        EarlyPauseWeighIn.PoisonFails = true;
    }

    private async Task<MartenDatabase> buildStorageAsync()
    {
        StoreOptions(opts => opts.Projections.Snapshot<EarlyPauseWeighIn>(SnapshotLifecycle.Async));

        var database = (MartenDatabase)theStore.Storage.Database;
        await database.EnsureStorageExistsAsync(typeof(IEvent), Token);
        return database;
    }

    private static ShardState paused(string shard, long sequence, ShardFailureCategory category)
    {
        var failure = new ShardFailure
        {
            Category = category,
            ExceptionType = "JasperFx.Events.Daemon.ParkedLimitReachedException",
            RootExceptionType = "JasperFx.Events.Daemon.ParkedLimitReachedException",
            Message = "Paused instead of parking",
            Detail = "JasperFx.Events.Daemon.ParkedLimitReachedException: Paused instead of parking",
            OccurredAt = DateTimeOffset.UtcNow,
            Event = new EventFailureDetails { Sequence = 2, EventTypeName = "early_pause_weighed" }
        };

        return new ShardState(shard, sequence)
        {
            Action = ShardAction.Paused,
            AgentStatus = "Paused",
            PauseReason = failure.Detail,
            Failure = failure,
            LastHeartbeat = DateTimeOffset.UtcNow
        };
    }

    private async Task<ShardState?> progressionAsync(string shard)
    {
        return (await theStore.Storage.Database.AllProjectionProgress(Token)).SingleOrDefault(x => x.ShardName == shard);
    }

    private async Task<ShardState> waitForTheFailureAsync(string shard)
    {
        var deadline = DateTimeOffset.UtcNow.Add(30.Seconds());
        while (true)
        {
            var state = await progressionAsync(shard);
            if (state?.Failure != null) return state;

            DateTimeOffset.UtcNow.ShouldBeLessThan(deadline, $"The failure of {shard} was never written");
            await Task.Delay(100.Milliseconds(), Token);
        }
    }

    // The first commit of a shard that starts at 0
    private async Task commitFromZeroAsync(long ceiling)
    {
        TheShardName.Identity.ShouldBe(TheShard);

        await using var session = theStore.LightweightSession();
        session.QueueOperation(new InsertProjectionProgress(theStore.Events, new EventRange(TheShardName, ceiling)));
        await session.SaveChangesAsync(Token);
    }

    [Fact]
    public async Task a_failure_with_no_row_creates_the_row_at_the_start_position()
    {
        var database = await buildStorageAsync();

        await database.WriteExtendedProgressionAsync(paused(TheShard, 0, ShardFailureCategory.ParkedLimitReached), Token);

        var state = (await progressionAsync(TheShard)).ShouldNotBeNull();
        state.Sequence.ShouldBe(0);
        state.AgentStatus.ShouldBe("Paused");
        state.Failure.ShouldNotBeNull().Category.ShouldBe(ShardFailureCategory.ParkedLimitReached);
        state.Failure.Event.ShouldNotBeNull().Sequence.ShouldBe(2);
    }

    [Fact]
    public async Task a_failure_with_no_row_uses_the_floor_the_shard_started_from()
    {
        var database = await buildStorageAsync();

        await database.WriteExtendedProgressionAsync(paused(TheShard, 7, ShardFailureCategory.ApplyEvent), Token);

        var state = (await progressionAsync(TheShard)).ShouldNotBeNull();
        state.Sequence.ShouldBe(7);
        state.Failure.ShouldNotBeNull().Category.ShouldBe(ShardFailureCategory.ApplyEvent);
    }

    [Fact]
    public async Task a_failure_never_moves_a_committed_position()
    {
        var database = await buildStorageAsync();
        await commitFromZeroAsync(25);

        await database.WriteExtendedProgressionAsync(paused(TheShard, 3, ShardFailureCategory.ApplyEvent), Token);

        var state = (await progressionAsync(TheShard)).ShouldNotBeNull();
        state.Sequence.ShouldBe(25);
        state.Failure.ShouldNotBeNull().Category.ShouldBe(ShardFailureCategory.ApplyEvent);
    }

    [Fact]
    public async Task the_first_commit_replaces_the_failure_row_at_zero()
    {
        var database = await buildStorageAsync();
        await database.WriteExtendedProgressionAsync(paused(TheShard, 0, ShardFailureCategory.ApplyEvent), Token);

        await commitFromZeroAsync(12);

        var state = (await progressionAsync(TheShard)).ShouldNotBeNull();
        state.Sequence.ShouldBe(12);
        state.Failure.ShouldBeNull();
    }

    [Fact]
    public async Task a_first_commit_still_fails_on_a_row_another_daemon_committed()
    {
        await buildStorageAsync();
        await commitFromZeroAsync(12);

        await Should.ThrowAsync<Exception>(() => commitFromZeroAsync(20));

        (await theStore.Advanced.ProjectionProgressFor(TheShardName, token: Token)).ShouldBe(12);
    }

    [Fact]
    public async Task a_shard_paused_at_the_parking_cap_on_its_first_batch_records_it_and_starts_from_zero_again()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Errors.ApplyErrors = ProjectionErrorAction.Park;
            opts.Projections.Errors.MaxParkedIdentitiesPerShard = 1;
            opts.Projections.Snapshot<EarlyPauseWeighIn>(SnapshotLifecycle.Async);
        });

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        // Two failing identities in the shard's first batch pass a cap of one, before anything has committed
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<EarlyPauseWeighIn>(first, new EarlyPauseWeighed(true));
            session.Events.StartStream<EarlyPauseWeighIn>(second, new EarlyPauseWeighed(true));
            await session.SaveChangesAsync(Token);
        }

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();

            var state = await waitForTheFailureAsync(TheShard);
            state.Sequence.ShouldBe(0);
            state.Failure!.Category.ShouldBe(ShardFailureCategory.ParkedLimitReached);

            await daemon.StopAllAsync();
        }

        // The projection is fixed. The shard starts where it would have without the row, from the beginning.
        EarlyPauseWeighIn.PoisonFails = false;

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await theStore.WaitForNonStaleProjectionDataAsync(30.Seconds());
            await daemon.StopAllAsync();
        }

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<EarlyPauseWeighIn>(first, Token)).ShouldNotBeNull().Count.ShouldBe(1);
        (await query.LoadAsync<EarlyPauseWeighIn>(second, Token)).ShouldNotBeNull().Count.ShouldBe(1);

        var progress = (await progressionAsync(TheShard)).ShouldNotBeNull();
        progress.Sequence.ShouldBe(2);
        progress.Failure.ShouldBeNull();
    }

    [Fact]
    public async Task a_from_present_subscription_that_fails_before_its_first_commit_starts_from_present_after_a_restart()
    {
        var subscription = new EarlyFailingSubscription();
        subscription.Options.SubscribeFromPresent();

        StoreOptions(opts => opts.Events.Subscribe(subscription));
        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), Token);

        // Started on an empty store, so "present" is 0 and the subscription has no row
        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();

            await appendAsync(2);

            var state = await waitForTheFailureAsync("EarlyFailing:All");
            state.Sequence.ShouldBe(0);

            await daemon.StopAllAsync();
        }

        subscription.Fails = false;

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();

            await appendAsync(1);
            await theStore.WaitForNonStaleProjectionDataAsync(30.Seconds());

            await daemon.StopAllAsync();
        }

        // Only the event appended after the restart: it started from the present (2), not from the row at 0
        subscription.Processed.ShouldBe([3L]);
        (await progressionAsync("EarlyFailing:All")).ShouldNotBeNull().Sequence.ShouldBe(3);
    }

    [Fact]
    public async Task a_from_present_subscription_keeps_its_present_mark_when_it_fails_before_its_first_commit()
    {
        var subscription = new EarlyFailingSubscription();
        subscription.Options.SubscribeFromPresent();

        StoreOptions(opts => opts.Events.Subscribe(subscription));
        await theStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent), Token);

        await appendAsync(3);

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        await appendAsync(1);

        var state = await waitForTheFailureAsync("EarlyFailing:All");
        state.Sequence.ShouldBe(3);

        await daemon.StopAllAsync();
    }

    private async Task appendAsync(int count)
    {
        await using var session = theStore.LightweightSession();
        session.Events.StartStream(Guid.NewGuid(), Enumerable.Range(0, count).Select(_ => (object)new EarlyPauseWeighed(false)).ToArray());
        await session.SaveChangesAsync(Token);
    }
}
