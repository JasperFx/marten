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
using Microsoft.Extensions.Logging;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// Under <c>UseTenantPartitionedEvents</c> the daemon runs one agent per tenant
/// (<c>{Name}:{ShardKey}:{tenantId}</c>). A subscription whose starting position seeds its progression
/// row before it runs -- <c>SubscribeFromPresent()</c>, <c>SubscribeFromSequence()</c>,
/// <c>SubscribeFromTime()</c>, <c>SubscribeAsInlineToAsync()</c> -- has the daemon hand
/// <c>IEventStore.RewindSubscriptionProgressAsync</c> that tenant agent's OWN identity (jasperfx#487 /
/// wolverine#3280). Marten resolved the argument against the store-global shards only and threw
/// "Unknown subscription name", so no tenant agent of such a subscription ever started: neither from
/// <c>StartAllAsync</c> nor from <c>StartAgentAsync(ShardName)</c>, which is how Wolverine-managed
/// distribution starts each tenant's agent.
/// </summary>
public class per_tenant_subscription_starting_position: PartitionedStoreContext
{
    private const string SubscriptionName = "TenantProbe";

    private readonly RecordingSubscription _subscription = new(SubscriptionName);

    protected override string SchemaPrefix => "tp_tsp";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Events.AddEventType<TenantProbeEvent>();

        _subscription.Options.SubscribeFromPresent();
        opts.Events.Subscribe(_subscription);
    }

    [Fact]
    public async Task start_all_starts_a_subscribe_from_present_agent_for_every_tenant()
    {
        var token = TestContext.Current.CancellationToken;
        var (alpha, beta) = await twoTenantsWithHistoryAsync(3, 5, token);

        var logger = new RecordingLogger();
        using var daemon = await Store.BuildProjectionDaemonAsync(logger: logger);
        await daemon.StartAllAsync();

        // StartAllAsync logs a failed agent start rather than throwing it
        logger.Errors.ShouldBeEmpty();

        runningIdentities(daemon).ShouldBe([identityFor(alpha), identityFor(beta)], ignoreOrder: true);

        // "Present" is each tenant's own height, so neither tenant's history is delivered...
        (await progressForAsync(alpha, token)).ShouldBe(3);
        (await progressForAsync(beta, token)).ShouldBe(5);

        // ...and what each tenant appends afterwards is
        await appendAsync(alpha, 1, token);
        await appendAsync(beta, 1, token);

        await waitForAsync(() => _subscription.Seen.Count >= 2, token);
        _subscription.Seen.ShouldBe([(alpha, 4L), (beta, 6L)], ignoreOrder: true);
    }

    [Fact]
    public async Task a_tenant_agent_started_by_its_shard_name_seeds_from_that_tenants_height()
    {
        var token = TestContext.Current.CancellationToken;
        var (alpha, beta) = await twoTenantsWithHistoryAsync(3, 5, token);

        using var daemon = await Store.BuildProjectionDaemonAsync();

        // Wolverine-managed distribution starts every tenant's agent on its own, by its shard name
        var agent = await daemon.StartAgentAsync(ShardName.Compose(SubscriptionName, ShardName.All, beta), token);

        agent.Status.ShouldBe(AgentStatus.Running);
        (await progressForAsync(beta, token)).ShouldBe(5);
        (await progressForAsync(alpha, token)).ShouldBe(0);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(3L)]
    public async Task rewinding_a_tenant_shard_moves_only_that_tenants_row_and_dead_letters(long floor)
    {
        var token = TestContext.Current.CancellationToken;
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await Store.Advanced.AddMartenManagedTenantsAsync(token, alpha, beta);

        var eventStore = (IEventStore<IDocumentOperations, IQuerySession>)Store;
        var database = (IEventDatabase)Store.Storage.Database;

        await eventStore.RewindAgentProgressAsync(database, identityFor(alpha), token, 5);
        await eventStore.RewindAgentProgressAsync(database, identityFor(beta), token, 5);
        await storeDeadLettersAsync(alpha, token, 2, 4);
        await storeDeadLettersAsync(beta, token, 2, 4);

        await eventStore.RewindSubscriptionProgressAsync(database, identityFor(alpha), token, floor);

        (await progressForAsync(alpha, token)).ShouldBe(floor);
        (await deadLetterSequencesAsync(alpha, token)).ShouldBe(new long[] { 2, 4 }.Where(x => x < floor).ToArray());

        // The other tenant draws its own sequence: the floor means nothing to it
        (await progressForAsync(beta, token)).ShouldBe(5);
        (await deadLetterSequencesAsync(beta, token)).ShouldBe([2L, 4L]);
    }

    private static string identityFor(string tenantId) =>
        ShardName.Compose(SubscriptionName, ShardName.All, tenantId).Identity;

    private static string[] runningIdentities(IProjectionDaemon daemon) => daemon.CurrentAgents()
        .Where(x => x.Status == AgentStatus.Running)
        .Select(x => x.Name.Identity)
        .ToArray();

    private async Task<(string Alpha, string Beta)> twoTenantsWithHistoryAsync(int alphaEvents, int betaEvents,
        CancellationToken token)
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await Store.Advanced.AddMartenManagedTenantsAsync(token, alpha, beta);

        await appendAsync(alpha, alphaEvents, token);
        await appendAsync(beta, betaEvents, token);

        return (alpha, beta);
    }

    private async Task appendAsync(string tenantId, int count, CancellationToken token)
    {
        await using var session = Store.LightweightSession(tenantId);
        session.Events.StartStream(Guid.NewGuid(),
            Enumerable.Range(1, count).Select(i => (object)new TenantProbeEvent(i)).ToArray());
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

    public record TenantProbeEvent(int Number);

    public class RecordingSubscription: SubscriptionBase
    {
        private readonly ConcurrentQueue<(string TenantId, long Sequence)> _seen = new();

        public RecordingSubscription(string name)
        {
            Name = name;
        }

        public IReadOnlyList<(string TenantId, long Sequence)> Seen => _seen.ToArray();

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

    private class RecordingLogger: ILogger
    {
        private readonly List<string> _errors = new();

        public IReadOnlyList<string> Errors
        {
            get
            {
                lock (_errors) return _errors.ToList();
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error) return;

            lock (_errors) _errors.Add($"{formatter(state, exception)} -> {exception?.GetType().Name}: {exception?.Message}");
        }
    }
}
