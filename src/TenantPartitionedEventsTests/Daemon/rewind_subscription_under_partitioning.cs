#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Subscriptions;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// Rewinding a subscription under <c>UseTenantPartitionedEvents</c>, end to end, where the daemon runs one agent per
/// tenant and every tenant draws its own event sequence. jasperfx#1025 (JasperFx 2.91.0) rewinds each tenant through
/// <c>RewindSubscriptionProgressAsync</c> with that tenant shard's own identity, which Marten accepts since #5700;
/// <see cref="rewind_subscription_per_tenant_pin" /> pins a per-tenant rewind of a projection to the beginning. These
/// facts cover the rest against a live store: rewinding every tenant to the beginning restarts each tenant's own
/// agent over that tenant's own events; rewinding one tenant to a sequence floor moves only that tenant, deletes only
/// its dead letters at or above the floor, and leaves the other tenant's agent running; and a sequence number, which
/// identifies a position in one tenant's events only, is refused for a rewind of every tenant and changes nothing.
/// </summary>
public class rewind_subscription_under_partitioning: PartitionedStoreContext
{
    private const string SubscriptionName = "RewindProbe";

    private readonly RecordingSubscription _subscription = new(SubscriptionName);

    protected override string SchemaPrefix => "tp_rws";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Events.AddEventType<RewindProbeEvent>();
        opts.Events.Subscribe(_subscription);
    }

    [Fact]
    public async Task rewinding_every_tenant_to_the_beginning_replays_each_tenant_through_its_own_agent()
    {
        var token = TestContext.Current.CancellationToken;
        var (daemon, alpha, beta) = await caughtUpDaemonAsync(token);
        using var _ = daemon;

        await daemon.RewindSubscriptionAsync(SubscriptionName, token);

        runningIdentities(daemon).ShouldBe([identityFor(alpha), identityFor(beta)], ignoreOrder: true);

        await waitForAsync(() => _subscription.Seen.Count >= 8, token);
        _subscription.Seen.ShouldBe(
            Enumerable.Range(1, 5).Select(x => (alpha, (long)x)).Concat(Enumerable.Range(1, 3).Select(x => (beta, (long)x))),
            ignoreOrder: true);

        (await progressForAsync(alpha, token)).ShouldBe(5);
        (await progressForAsync(beta, token)).ShouldBe(3);
    }

    [Fact]
    public async Task rewinding_one_tenant_moves_only_that_tenant()
    {
        var token = TestContext.Current.CancellationToken;
        var (daemon, alpha, beta) = await caughtUpDaemonAsync(token);
        using var _ = daemon;

        await storeDeadLettersAsync(alpha, token, 1, 4);
        await storeDeadLettersAsync(beta, token, 2, 3);
        var betaAgent = daemon.CurrentAgents().Single(x => x.Name.Identity == identityFor(beta));

        await daemon.RewindSubscriptionAsync(SubscriptionName, alpha, token, sequenceFloor: 2);

        await waitForAsync(() => _subscription.Seen.Count >= 3, token);
        _subscription.Seen.ShouldBe([(alpha, 3L), (alpha, 4L), (alpha, 5L)], ignoreOrder: true);
        (await progressForAsync(alpha, token)).ShouldBe(5);
        (await deadLetterSequencesAsync(alpha, token)).ShouldBe([1L]);

        // The other tenant is not stopped, not rewound, and keeps its dead letters
        daemon.CurrentAgents().Single(x => x.Name.Identity == identityFor(beta)).ShouldBeSameAs(betaAgent);
        betaAgent.Status.ShouldBe(AgentStatus.Running);
        (await progressForAsync(beta, token)).ShouldBe(3);
        (await deadLetterSequencesAsync(beta, token)).ShouldBe([2L, 3L]);
    }

    [Fact]
    public async Task rewinding_every_tenant_to_a_sequence_number_is_refused_and_changes_nothing()
    {
        var token = TestContext.Current.CancellationToken;
        var (daemon, alpha, beta) = await caughtUpDaemonAsync(token);
        using var _ = daemon;

        await storeDeadLettersAsync(alpha, token, 4);
        await storeDeadLettersAsync(beta, token, 2, 3);
        var before = daemon.CurrentAgents().ToArray();

        // Sequence 2 is a position in one tenant's events: it names nothing in any other tenant's sequence
        var ex = await Record.ExceptionAsync(() =>
            daemon.RewindSubscriptionAsync(SubscriptionName, token, sequenceFloor: 2));

        (await deadLetterSequencesAsync(beta, token)).ShouldBe([2L, 3L]);
        (await deadLetterSequencesAsync(alpha, token)).ShouldBe([4L]);
        daemon.CurrentAgents().ShouldBe(before);
        runningIdentities(daemon).ShouldBe([identityFor(alpha), identityFor(beta)], ignoreOrder: true);
        (await progressForAsync(alpha, token)).ShouldBe(5);
        (await progressForAsync(beta, token)).ShouldBe(3);

        ex.ShouldBeOfType<ArgumentOutOfRangeException>();
    }

    // Two tenants at different heights, both delivered once, then the recording is cleared
    private async Task<(IProjectionDaemon Daemon, string Alpha, string Beta)> caughtUpDaemonAsync(
        CancellationToken token)
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await Store.Advanced.AddMartenManagedTenantsAsync(token, alpha, beta);
        await appendAsync(alpha, 5, token);
        await appendAsync(beta, 3, token);

        var daemon = await Store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        await waitForAsync(() => _subscription.Seen.Count >= 8, token);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < 30.Seconds() &&
               (await progressForAsync(alpha, token) < 5 || await progressForAsync(beta, token) < 3))
        {
            await Task.Delay(100, token);
        }

        _subscription.Seen.Count.ShouldBe(8);
        runningIdentities(daemon).ShouldBe([identityFor(alpha), identityFor(beta)], ignoreOrder: true);
        _subscription.Clear();

        return (daemon, alpha, beta);
    }

    private static string identityFor(string tenantId) =>
        ShardName.Compose(SubscriptionName, ShardName.All, tenantId).Identity;

    private static string[] runningIdentities(IProjectionDaemon daemon) => daemon.CurrentAgents()
        .Where(x => x.Status == AgentStatus.Running)
        .Select(x => x.Name.Identity)
        .ToArray();

    private async Task appendAsync(string tenantId, int count, CancellationToken token)
    {
        await using var session = Store.LightweightSession(tenantId);
        session.Events.StartStream(Guid.NewGuid(),
            Enumerable.Range(1, count).Select(i => (object)new RewindProbeEvent(i)).ToArray());
        await session.SaveChangesAsync(token);
    }

    private Task<long> progressForAsync(string tenantId, CancellationToken token) =>
        ((IEventDatabase)Store.Storage.Database)
        .ProjectionProgressFor(ShardName.Compose(SubscriptionName, ShardName.All, tenantId), token);

    private async Task storeDeadLettersAsync(string tenantId, CancellationToken token, params long[] sequences)
    {
        await using var session = Store.LightweightSession();
        foreach (var sequence in sequences)
        {
            session.Store(new DeadLetterEvent
            {
                Id = Guid.CreateVersion7(),
                ProjectionName = SubscriptionName,
                ShardName = ShardName.All,
                TenantId = tenantId,
                EventSequence = sequence,
                Timestamp = DateTimeOffset.UtcNow,
                ExceptionMessage = "probe",
                ExceptionType = "Probe"
            });
        }

        await session.SaveChangesAsync(token);
    }

    private async Task<long[]> deadLetterSequencesAsync(string tenantId, CancellationToken token)
    {
        var rows = await ((IEventDatabase)Store.Storage.Database)
            .QueryDeadLetterEventsAsync(new ShardName(SubscriptionName), tenantId, 0, 100, token);

        return rows.Select(x => x.EventSequence).OrderBy(x => x).ToArray();
    }

    private static async Task waitForAsync(Func<bool> condition, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.Elapsed < 30.Seconds())
        {
            await Task.Delay(100, token);
        }
    }

    public record RewindProbeEvent(int Number);

    public class RecordingSubscription: SubscriptionBase
    {
        private readonly ConcurrentQueue<(string TenantId, long Sequence)> _seen = new();

        public RecordingSubscription(string name)
        {
            Name = name;
        }

        public IReadOnlyList<(string TenantId, long Sequence)> Seen => _seen.ToArray();

        public void Clear() => _seen.Clear();

        public override Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
            IDocumentOperations operations, CancellationToken cancellationToken)
        {
            foreach (var e in page.Events)
            {
                _seen.Enqueue((e.TenantId, e.Sequence));
            }

            return Task.FromResult(NullChangeListener.Instance);
        }
    }
}
