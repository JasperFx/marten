using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Subscriptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests.Subscriptions;

/// <summary>
/// #5610 / jasperfx#953. A subscription's drain is now bounded by
/// <c>Projections.StopAndDrainTimeout</c>. A page still running when that expires is cancelled and its
/// transaction — the subscription's writes through the Marten session AND the progression update —
/// rolls back together, so the page is processed again by whichever node runs the shard next.
///
/// <para>
/// This is the behaviour change users will feel, so it is pinned here rather than left to the upstream
/// suite: delivery was always at-least-once, but a subscription whose pages outlast the timeout now sees
/// duplicates on <em>every</em> shutdown and rebalance. The fix for a slow subscription is to raise the
/// timeout, which the second test demonstrates.
/// </para>
/// </summary>
public class slow_subscription_pages_are_cancelled_on_drain: OneOffConfigurationsContext
{
    /// <summary>
    /// Blocks inside ProcessEventsAsync until released, recording every page it was handed and whether
    /// that page ran to completion. The distinction is the whole point: a page that STARTED but never
    /// COMPLETED is one whose transaction rolled back.
    /// </summary>
    private sealed class BlockingSubscription: SubscriptionBase
    {
        private readonly TimeSpan _delay;
        public List<long> PagesStarted { get; } = new();
        public List<long> PagesCompleted { get; } = new();

        public BlockingSubscription(TimeSpan delay)
        {
            _delay = delay;
            Name = "Blocking";
        }

        public override async Task<IChangeListener> ProcessEventsAsync(EventRange page,
            ISubscriptionController controller, IDocumentOperations operations,
            CancellationToken cancellationToken)
        {
            lock (PagesStarted) PagesStarted.Add(page.SequenceCeiling);

            // Deliberately honours the token: that is what the daemon cancels at the timeout, and a
            // subscription that ignored it would simply run on past the bound.
            await Task.Delay(_delay, cancellationToken);

            lock (PagesCompleted) PagesCompleted.Add(page.SequenceCeiling);

            return NullChangeListener.Instance;
        }
    }

    private async Task appendSomeEventsAsync()
    {
        await using var session = theStore.LightweightSession();
        for (var i = 0; i < 10; i++)
        {
            session.Events.StartStream(Guid.NewGuid(), new EventSourcingTests.Aggregation.AEvent(),
                new EventSourcingTests.Aggregation.BEvent());
        }

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task a_page_slower_than_the_timeout_is_rolled_back_and_redelivered()
    {
        // The page takes far longer than the drain is allowed to wait for it.
        var subscription = new BlockingSubscription(30.Seconds());
        StoreOptions(opts =>
        {
            opts.Projections.StopAndDrainTimeout = 1.Seconds();
            opts.Projections.Subscribe(subscription);
        });

        await appendSomeEventsAsync();

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();

            // Let the subscription pick up a page and get stuck inside it.
            await Task.Delay(1.Seconds(), TestContext.Current.CancellationToken);

            // Bounded by StopAndDrainTimeout rather than by the 30 second page.
            await daemon.StopAllAsync();
        }

        subscription.PagesStarted.ShouldNotBeEmpty();

        // Cancelled, so it never finished -- and therefore its progression never committed.
        subscription.PagesCompleted.ShouldBeEmpty();

        var progress = await theStore.Advanced.AllProjectionProgress(token: TestContext.Current.CancellationToken);
        progress.ShouldNotContain(x => x.ShardName.Contains("Blocking") && x.Sequence > 0);
    }

    [Fact]
    public async Task raising_the_timeout_lets_the_page_finish_instead()
    {
        // The documented fix for a slow subscription: give the drain long enough to let the page land.
        var subscription = new BlockingSubscription(1.Seconds());
        StoreOptions(opts =>
        {
            opts.Projections.StopAndDrainTimeout = 30.Seconds();
            opts.Projections.Subscribe(subscription);
        });

        await appendSomeEventsAsync();

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await daemon.WaitForNonStaleData(30.Seconds());
            await daemon.StopAllAsync();
        }

        subscription.PagesCompleted.ShouldNotBeEmpty();

        var progress = await theStore.Advanced.AllProjectionProgress(token: TestContext.Current.CancellationToken);
        progress.ShouldContain(x => x.ShardName.Contains("Blocking") && x.Sequence > 0);
    }
}
