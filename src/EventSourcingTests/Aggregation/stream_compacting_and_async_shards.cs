using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using JasperFx.Events.Aggregation;
using Marten.Subscriptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

// #6050: compaction replaces a stream's last event with a Compacted<T> and deletes the events below it. A
// single-stream projection of T fast-forwards over the snapshot, but an async multi-stream projection or a
// subscription that had not reached those events yet never saw them at all.
public class stream_compacting_and_async_shards: OneOffConfigurationsContext
{
    public sealed record Counted(Guid TallyId, int Amount);

    public sealed class Counter
    {
        public Guid Id { get; set; }
        public int Total { get; set; }

        public static Counter Create(Counted e) => new() { Total = e.Amount };

        public void Apply(Counted e) => Total += e.Amount;
    }

    public sealed class Tally
    {
        public Guid Id { get; set; }
        public int Total { get; set; }
        public int Events { get; set; }
    }

    public sealed class TallyProjection: MultiStreamProjection<Tally, Guid>
    {
        public TallyProjection()
        {
            Identity<Counted>(x => x.TallyId);
        }

        public void Apply(Tally tally, Counted e)
        {
            tally.Total += e.Amount;
            tally.Events++;
        }
    }

    public sealed class CountingSubscription: SubscriptionBase
    {
        public ConcurrentBag<long> Sequences { get; } = new();

        public override Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
            IDocumentOperations operations, CancellationToken cancellationToken)
        {
            foreach (var e in page.Events.Where(x => x.Data is Counted)) Sequences.Add(e.Sequence);
            return Task.FromResult(NullChangeListener.Instance);
        }
    }

    private readonly CountingSubscription _subscription = new();

    private async Task<(Guid Stream, Guid Tally)> threeCountedEvents()
    {
        var stream = Guid.NewGuid();
        var tally = Guid.NewGuid();

        await using var session = theStore.LightweightSession();
        session.Events.StartStream<Counter>(stream, new Counted(tally, 1), new Counted(tally, 2), new Counted(tally, 3));
        await session.SaveChangesAsync();

        return (stream, tally);
    }

    private async Task compact(Guid stream)
    {
        await using var session = theStore.LightweightSession();
        await session.Events.CompactStreamAsync<Counter>(stream);
        await session.SaveChangesAsync();
    }

    private async Task<int> countedEventsIn(Guid stream)
    {
        await using var query = theStore.QuerySession();
        var events = await query.Events.FetchStreamAsync(stream);
        return events.Count(x => x.Data is Counted);
    }

    private async Task catchUp()
    {
        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(TimeSpan.FromSeconds(30));
        await daemon.StopAllAsync();
    }

    [Fact]
    public async Task compaction_is_refused_while_an_async_projection_and_a_subscription_have_not_processed_the_events()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new TallyProjection(), ProjectionLifecycle.Async);
            opts.Events.Subscribe(_subscription);
        });
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var (stream, tallyId) = await threeCountedEvents();

        // Before the guard this compacted, and the projection and the subscription never saw the three events:
        // the Tally was never created and the subscription saw nothing
        var refused = await Should.ThrowAsync<InvalidOperationException>(() => compact(stream));
        refused.Message.ShouldContain(nameof(Tally));
        refused.Message.ShouldContain(typeof(CountingSubscription).Name);

        (await countedEventsIn(stream)).ShouldBe(3);

        await catchUp();

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<Tally>(tallyId))!.Events.ShouldBe(3);
        _subscription.Sequences.Count.ShouldBe(3);
    }

    [Fact]
    public async Task compaction_is_refused_while_only_a_subscription_is_behind()
    {
        StoreOptions(opts => opts.Events.Subscribe(_subscription));
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var (stream, _) = await threeCountedEvents();

        await Should.ThrowAsync<InvalidOperationException>(() => compact(stream));
        (await countedEventsIn(stream)).ShouldBe(3);
    }

    [Fact]
    public async Task compaction_proceeds_once_every_shard_has_processed_the_events()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add(new TallyProjection(), ProjectionLifecycle.Async);
            opts.Events.Subscribe(_subscription);
        });
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var (stream, tallyId) = await threeCountedEvents();
        await catchUp();

        await compact(stream);
        (await countedEventsIn(stream)).ShouldBe(0);

        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(stream, new Counted(tallyId, 4), new Counted(tallyId, 5));
            await session.SaveChangesAsync();
        }

        await catchUp();

        await using var query = theStore.QuerySession();
        var tally = await query.LoadAsync<Tally>(tallyId);
        tally!.Events.ShouldBe(5);
        tally.Total.ShouldBe(15);
        _subscription.Sequences.Count.ShouldBe(5);

        (await query.Events.AggregateStreamAsync<Counter>(stream))!.Total.ShouldBe(15);
    }

    [Fact]
    public async Task the_compacted_aggregates_own_async_projection_being_behind_does_not_block_compaction()
    {
        // A single-stream projection of the compacted type fast-forwards over the Compacted<T>
        StoreOptions(opts => opts.Projections.Snapshot<Counter>(SnapshotLifecycle.Async));
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var (stream, _) = await threeCountedEvents();

        await compact(stream);
        (await countedEventsIn(stream)).ShouldBe(0);

        await catchUp();

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<Counter>(stream))!.Total.ShouldBe(6);
    }

    [Fact]
    public async Task another_aggregates_async_projection_of_the_stream_still_blocks_compaction()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<Counter>(SnapshotLifecycle.Async);
            opts.Projections.Snapshot<OtherView>(SnapshotLifecycle.Async);
        });
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var (stream, _) = await threeCountedEvents();

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => compact(stream));
        refused.Message.ShouldContain(nameof(OtherView));
        refused.Message.ShouldNotContain(nameof(Counter) + ":");
    }

    public sealed class OtherView
    {
        public Guid Id { get; set; }
        public int Count { get; set; }

        public static OtherView Create(Counted e) => new() { Count = 1 };

        public void Apply(Counted e) => Count++;
    }
}
