using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace Marten.Subscriptions;

internal class SubscriptionExecution: ISubscriptionExecution
{
    private readonly ISubscription _subscription;
    private readonly DocumentStore _store;
    private readonly IMartenDatabase _database;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ActionBlock<EventRange> _executionBlock;
    private readonly SubscriptionMetrics _metrics;

    public SubscriptionExecution(ShardName shard, ISubscription subscription, DocumentStore store, IMartenDatabase database,
        ILogger logger)
    {
        _subscription = subscription;
        _store = store;
        _database = database;
        _logger = logger;

        ShardIdentity = shard.Identity;
        if (database.Identifier != "Marten")
        {
            ShardIdentity += $"@{database.Identifier}";
        }

        _executionBlock = new ActionBlock<EventRange>(executeRange, _cancellation.Token.SequentialOptions());
    }

    private async Task executeRange(EventRange range)
    {
        if (_cancellation.IsCancellationRequested) return;

        using var activity = range.Agent.Metrics.TrackExecution(range);

        try
        {
            await using var parent = (DocumentSessionBase)_store.OpenSession(SessionOptions.ForDatabase(_database));

            var batch = new ProjectionUpdateBatch(_store.Events, _store.Options.Projections, parent,
                range, _cancellation.Token, Mode);

            await using var session = new ProjectionDocumentSession(_store, batch,
                new SessionOptions
                {
                    Tracking = DocumentTracking.IdentityOnly,
                    Tenant = new Tenant(Tenancy.DefaultTenantId, _database)
                }, Mode);


            var listener = await _subscription.ProcessEventsAsync(range, range.Agent, session, _cancellation.Token)
                .ConfigureAwait(false);

            batch.Listeners.Add(listener);
            await batch.WaitForCompletion().ConfigureAwait(false);

            // Polly is already around the basic retry here, so anything that gets past this
            // probably deserves a full circuit break
            await session.ExecuteBatchAsync(batch, _cancellation.Token).ConfigureAwait(false);

            range.Agent.MarkSuccess(range.SequenceCeiling);

            if (Mode == ShardExecutionMode.Continuous)
            {
                _logger.LogInformation("Subscription '{ShardIdentity}': Executed for {Range}",
                    ShardIdentity, batch.Range);
            }

            range.Agent.Metrics.UpdateProcessed(range.Size);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            activity?.RecordException(e);
            _logger.LogError(e, "Error trying to process subscription {Name}", ShardIdentity);
            await range.Agent.ReportCriticalFailureAsync(e).ConfigureAwait(false);
        }
        finally
        {
            activity?.Stop();
        }
    }

    public string ShardIdentity { get; }

    public async ValueTask DisposeAsync()
    {
        await _subscription.DisposeAsync().ConfigureAwait(false);
    }

    public void Enqueue(EventPage page, ISubscriptionAgent subscriptionAgent)
    {
        if (_cancellation.IsCancellationRequested) return;

        var range = new EventRange(subscriptionAgent.Name, page.Floor, page.Ceiling)
        {
            Agent = subscriptionAgent,
            Events = page
        };

        _executionBlock.Post(range);
    }

    public async Task StopAndDrainAsync(CancellationToken token)
    {
        _executionBlock.Complete();
        await _executionBlock.Completion.ConfigureAwait(false);
#if NET8_0_OR_GREATER
        await _cancellation.CancelAsync().ConfigureAwait(false);
#else
        _cancellation.Cancel();
#endif
    }

    public async Task HardStopAsync()
    {
        _executionBlock.Complete();
#if NET8_0_OR_GREATER
        await _cancellation.CancelAsync().ConfigureAwait(false);
#else
        _cancellation.Cancel();
#endif
    }

    public Task EnsureStorageExists()
    {
        return Task.CompletedTask;
    }

    public string DatabaseName => _database.Identifier;
    public ShardExecutionMode Mode { get; set; } = ShardExecutionMode.Continuous;
}
