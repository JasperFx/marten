using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Subscriptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests;

public class catching_up_mode_for_projections_and_subscriptions : OneOffConfigurationsContext, IAsyncLifetime
{
    private EventStoreStatistics statistics;
    private IReadOnlyList<ShardState> progress;
    private Guid streamId;

    public async Task InitializeAsync()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<LetterCountsProjection>(ProjectionLifecycle.Async);
            opts.Events.Subscribe(new LetterEventsSubscription());
            opts.Projections.Add<ADocEventProjection>(ProjectionLifecycle.Async);
        });

        await theStore.Advanced.ResetAllData();
        LetterEventsSubscription.Clear();

        streamId = theSession.Events.StartStream<LetterCounts>("ABCDEABBBBC".ToLetterEvents()).Id;
        theSession.Events.StartStream<LetterCounts>("ABCBBC".ToLetterEvents());
        theSession.Events.StartStream<LetterCounts>("AEEEBBC".ToLetterEvents());
        theSession.Events.StartStream<LetterCounts>("AEEEEBBBC".ToLetterEvents());
        theSession.Events.StartStream<LetterCounts>("ACCCCCCBBC".ToLetterEvents());

        await theSession.SaveChangesAsync();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.CatchUpAsync(CancellationToken.None);

        statistics = await theStore.Advanced.FetchEventStoreStatistics();
        progress = await theStore.Advanced.AllProjectionProgress();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task all_shards_advanced_to_the_high_water_mark()
    {
        foreach (var shardState in progress)
        {
            shardState.Sequence.ShouldBe(statistics.EventSequenceNumber);
        }
    }

    [Fact]
    public async Task have_the_expected_documents_from_EventProjection()
    {
        // EventProjection ran
        (await theSession.Query<ADoc>().CountAsync()).ShouldBe(6);

        var sequences = (await theSession.Events.QueryAllRawEvents().ToListAsync())
            .OfType<IEvent<AEvent>>().Select(x => x.Sequence).ToArray();

        var actuals = await theSession.Query<ADoc>().Select(x => x.Id).ToListAsync();

        actuals.ShouldBe(sequences);
    }

    [Fact]
    public async Task aggregation_projection_can_catch_up()
    {
        // Aggregation too
        (await theSession.Query<EventSourcingTests.Aggregation.LetterCounts>().CountAsync()).ShouldBe(5);
        var counts = await theSession.LoadAsync<EventSourcingTests.Aggregation.LetterCounts>(streamId);
        counts.ACount.ShouldBe(2);
        counts.BCount.ShouldBe(5);
        counts.CCount.ShouldBe(2);
        counts.DCount.ShouldBe(1);
    }

    [Fact]
    public async Task subscription_catches_up()
    {
        // Subscription
        LetterEventsSubscription.Events.Count.ShouldBe((int)statistics.EventCount);
        LetterEventsSubscription.Events.Last().Sequence.ShouldBe(statistics.EventCount);
    }

}

public class ADoc
{
    public long Id { get; set; }
}

public class ADocEventProjection: EventProjection
{
    public void Project(IEvent<AEvent> e, IDocumentOperations ops)
    {
        ops.Store(new ADoc{Id = e.Sequence});
    }
}

public class LetterEventsSubscription: SubscriptionBase
{
    public static List<IEvent> Events { get; } = new();

    public static void Clear()
    {
        Events.Clear();
    }

    public override Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller, IDocumentOperations operations,
        CancellationToken cancellationToken)
    {
        Events.AddRange(page.Events);
        return Task.FromResult(NullChangeListener.Instance);
    }
}

public class LetterCountsProjection: SingleStreamProjection<LetterCounts, Guid>
{
    public override LetterCounts Evolve(LetterCounts snapshot, Guid id, IEvent e)
    {

        switch (e.Data)
        {
            case AEvent _:
                snapshot ??= new() { Id = id };
                snapshot.ACount++;
                break;

            case BEvent _:
                snapshot ??= new() { Id = id };
                snapshot.BCount++;
                break;

            case CEvent _:
                snapshot ??= new() { Id = id };
                snapshot.CCount++;
                break;

            case DEvent _:
                snapshot ??= new() { Id = id };
                snapshot.DCount++;
                break;
        }

        return snapshot;
    }
}


