using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Daemon.Internals;
using Marten.Events.Daemon.Resiliency;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Weasel.Core;

namespace Marten.Events.Daemon;

public partial class ProjectionDaemon : IProjectionDaemon, IObserver<ShardState>, IDaemonRuntime
{
    private readonly DocumentStore _store;
    private readonly IAgentFactory _factory;
    private readonly List<ISubscriptionAgent> _active = new();
    private CancellationTokenSource _cancellation = new();
    private readonly HighWaterAgent _highWater;
    private readonly IDisposable _breakSubscription;
    private RetryBlock<DeadLetterEvent> _deadLetterBlock;

    public ProjectionDaemon(DocumentStore store, MartenDatabase database, ILogger logger, IHighWaterDetector detector,
        IAgentFactory factory)
    {
        Database = database;
        _store = store;
        _factory = factory;
        Logger = logger;
        Tracker = Database.Tracker;
        _highWater = new HighWaterAgent(detector, Tracker, logger, store.Options.Projections, _cancellation.Token);

        _breakSubscription = database.Tracker.Subscribe(this);

        _deadLetterBlock = buildDeadLetterBlock();
    }

    private RetryBlock<DeadLetterEvent> buildDeadLetterBlock()
    {
        return new RetryBlock<DeadLetterEvent>(async (deadLetterEvent, token) =>
        {
            // More important to end cleanly
            if (token.IsCancellationRequested) return;

            await using var session =
                _store.LightweightSession(SessionOptions.ForDatabase(Database));

            await using (session.ConfigureAwait(false))
            {
                session.Store(deadLetterEvent);
                await session.SaveChangesAsync(_cancellation.Token).ConfigureAwait(false);
            }
        }, Logger, _cancellation.Token);
    }

    internal MartenDatabase Database { get; }

    internal ILogger Logger { get; }

    public void Dispose()
    {
        _cancellation?.Dispose();
        _highWater?.Dispose();
        _breakSubscription.Dispose();
        _deadLetterBlock.Dispose();
    }

    public ShardStateTracker Tracker { get; }
    public bool IsRunning => _highWater.IsRunning;


    private async Task startAgent(ISubscriptionAgent agent, ShardExecutionMode mode)
    {
        // Be idempotent, don't start an agent that is already running
        if (_active.Any(x => Equals(x.Name, agent.Name))) return;

        var position = mode == ShardExecutionMode.Continuous
            ? await Database.ProjectionProgressFor(agent.Name, _cancellation.Token).ConfigureAwait(false)

            // No point in doing the extra database hop
            : 0;

        var errorOptions = mode == ShardExecutionMode.Continuous
            ? _store.Options.Projections.Errors
            : _store.Options.Projections.RebuildErrors;

        await agent.StartAsync(new SubscriptionExecutionRequest(position, mode, errorOptions, this)).ConfigureAwait(false);
        agent.MarkHighWater(HighWaterMark());
        _active.Add(agent);
    }

    private async Task rebuildAgent(ISubscriptionAgent agent, long highWaterMark, TimeSpan shardTimeout)
    {
        // Ensure that the agent is stopped if it is already running
        await StopAsync(agent.Name.Identity).ConfigureAwait(false);

        var errorOptions = _store.Options.Projections.Errors;

        var request = new SubscriptionExecutionRequest(0, ShardExecutionMode.Rebuild, errorOptions, this);
        await agent.ReplayAsync(request, highWaterMark, shardTimeout).ConfigureAwait(false);



        _active.Add(agent);
    }

    public async Task StartShard(string shardName, CancellationToken token)
    {
        if (!_highWater.IsRunning)
        {
            await StartHighWaterDetectionAsync().ConfigureAwait(false);
        }

        var agent = _active.FirstOrDefault(x => x.Name.Identity == shardName);
        if (agent != null) return;

        agent = _factory.BuildAgentForShard(shardName, Database);
        await startAgent(agent, ShardExecutionMode.Continuous).ConfigureAwait(false);
    }

    public async Task StopAsync(string shardName, Exception ex = null)
    {
        var agent = _active.FirstOrDefault(x => x.Name.Identity == shardName);
        if (agent != null)
        {
            var cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(5.Seconds());

            try
            {
                await agent.StopAndDrainAsync(cancellation.Token).ConfigureAwait(true);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error trying to stop and drain a subscription agent for '{Name}'",
                    agent.Name.Identity);
            }
            finally
            {
                _active.Remove(agent);
            }
        }

        if (!_active.Any() && _highWater.IsRunning)
        {
            // Nothing happening, so might as well stop hammering the database!
            await _highWater.Stop().ConfigureAwait(false);
        }
    }

    public async Task StartAllAsync()
    {
        if (!_highWater.IsRunning)
        {
            await StartHighWaterDetectionAsync().ConfigureAwait(false);
        }

        var agents = _factory.BuildAllAgents(Database);
        foreach (var agent in agents)
        {
            await startAgent(agent, ShardExecutionMode.Continuous).ConfigureAwait(false);
        }
    }

    public async Task StopAllAsync()
    {
        await _highWater.Stop().ConfigureAwait(false);

        var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(5.Seconds());
        try
        {
            await Parallel.ForEachAsync(_active, cancellation.Token, (agent, t) => new ValueTask(agent.StopAndDrainAsync(t))).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error trying to stop subscription agents for {Agents}", _active.Select(x => x.Name.Identity).Join(", "));
        }

        try
        {
            await _deadLetterBlock.DrainAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error trying to finish all outstanding DeadLetterEvent persistence");
        }

        _active.Clear();

        _deadLetterBlock = buildDeadLetterBlock();
    }

    public async Task StartHighWaterDetectionAsync()
    {
        if (_store.Options.AutoCreateSchemaObjects != AutoCreate.None)
        {
            await Database.EnsureStorageExistsAsync(typeof(IEvent), _cancellation.Token).ConfigureAwait(false);
        }

        await _highWater.Start().ConfigureAwait(false);
    }

    public async Task WaitForNonStaleData(TimeSpan timeout)
    {
        var stopWatch = Stopwatch.StartNew();
        var statistics = await Database.FetchEventStoreStatistics(_cancellation.Token).ConfigureAwait(false);

        while (stopWatch.Elapsed < timeout)
        {
            if (_active.All(x => x.Position >= statistics.EventSequenceNumber))
            {
                return;
            }

            await Task.Delay(100.Milliseconds(), _cancellation.Token).ConfigureAwait(false);
        }

        var message = $"The active projection shards did not reach sequence {statistics.EventSequenceNumber} in time";
        throw new TimeoutException(message);
    }

    public AgentStatus StatusFor(string shardName)
    {
        var agent = _active.FirstOrDefault(x => x.Name.Identity == shardName);
        if (agent == null) return AgentStatus.Stopped;

        return agent.Status;
    }

    public IReadOnlyList<ISubscriptionAgent> CurrentAgents()
    {
        return _active;
    }

    public bool HasAnyPaused()
    {
        return _active.Any(x => x.Status == AgentStatus.Paused);
    }

    public void EjectPausedShard(string shardName)
    {
        _active.RemoveAll(x => x.Name.Identity == shardName && x.Status == AgentStatus.Paused);
    }

    public Task PauseHighWaterAgentAsync()
    {
        return _highWater.Stop();
    }

    public long HighWaterMark()
    {
        return Tracker.HighWaterMark;
    }

    void IObserver<ShardState>.OnCompleted()
    {
        // Nothing
    }

    void IObserver<ShardState>.OnError(Exception error)
    {
        // Nothing
    }

    void IObserver<ShardState>.OnNext(ShardState value)
    {
        if (value.ShardName == ShardState.HighWaterMark)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug("Event high water mark detected at {Sequence}", value.Sequence);
            }

            foreach (var agent in _active)
            {
                agent.MarkHighWater(value.Sequence);
            }
        }
    }

    public Task RecordDeadLetterEventAsync(DeadLetterEvent @event)
    {
        return _deadLetterBlock.PostAsync(@event);
    }
}
