#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Daemon.Progress;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon;

/// <summary>
///     The main class for running asynchronous projections
/// </summary>
internal class ProjectionDaemon: IProjectionDaemon
{
    private readonly Dictionary<string, ShardAgent> _agents = new();
    private readonly HighWaterAgent _highWater;
    private readonly ILogger _logger;
    private readonly DocumentStore _store;
    private CancellationTokenSource _cancellation;
    private INodeCoordinator? _coordinator;

    private bool _isStopping;
    private readonly RetryBlock<DeadLetterEvent> _deadLetterBlock;

    public ProjectionDaemon(DocumentStore store, IMartenDatabase database, IHighWaterDetector detector,
        ILogger logger)
    {
        _cancellation = new CancellationTokenSource();
        _store = store;
        Database = database;
        _logger = logger;

        Tracker = new ShardStateTracker(logger);
        _highWater = new HighWaterAgent(detector, Tracker, logger, store.Options.Projections, _cancellation.Token);

        Settings = store.Options.Projections;

        _deadLetterBlock = new RetryBlock<DeadLetterEvent>(async (deadLetterEvent, token) =>
        {
            await using var session =
                _store.LightweightSession(SessionOptions.ForDatabase(Database));

            await using (session.ConfigureAwait(false))
            {
                session.Store(deadLetterEvent);
                await session.SaveChangesAsync(_cancellation.Token).ConfigureAwait(false);
            }
        }, _logger, _cancellation.Token);
    }

    // Only for testing
    public ProjectionDaemon(DocumentStore store, ILogger logger): this(store, store.Tenancy.Default.Database,
        new HighWaterDetector((ISingleQueryRunner)store.Tenancy.Default.Database, store.Events, logger),
        logger)
    {
    }

    public IMartenDatabase Database { get; }

    public DaemonSettings Settings { get; }

    public bool IsRunning => _highWater.IsRunning;

    public ShardStateTracker Tracker { get; }

    public async Task StartDaemon()
    {
        await Database.EnsureStorageExistsAsync(typeof(IEvent), _cancellation.Token).ConfigureAwait(false);
        await _highWater.Start().ConfigureAwait(false);
    }

    public async Task WaitForNonStaleData(TimeSpan timeout)
    {
        var stopWatch = Stopwatch.StartNew();
        var statistics = await Database.FetchEventStoreStatistics(_cancellation.Token).ConfigureAwait(false);

        while (stopWatch.Elapsed < timeout)
        {
            if (CurrentShards().All(x => x.Position >= statistics.EventSequenceNumber))
            {
                return;
            }

            await Task.Delay(100.Milliseconds(), _cancellation.Token).ConfigureAwait(false);
        }

        var message = $"The active projection shards did not reach sequence {statistics.EventSequenceNumber} in time";
        throw new TimeoutException(message);
    }

    public Task PauseHighWaterAgent()
    {
        return _highWater.Stop();
    }

    public long HighWaterMark()
    {
        return Tracker.HighWaterMark;
    }

    public async Task StartAllShards()
    {
        if (!_highWater.IsRunning)
        {
            await StartDaemon().ConfigureAwait(false);
        }

        var shards = _store.Options.Projections.AllShards();
        foreach (var shard in shards)
            await StartShard(shard, ShardExecutionMode.Continuous, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task StartShard(string shardName, CancellationToken token)
    {
        if (!_highWater.IsRunning)
        {
            await StartDaemon().ConfigureAwait(false);
        }

        // Latch it so it doesn't double start
        if (_agents.ContainsKey(shardName))
        {
            return;
        }

        if (_store.Options.Projections.TryFindAsyncShard(shardName, out var shard))
        {
            await StartShard(shard, ShardExecutionMode.Continuous, token).ConfigureAwait(false);
        }
    }

    public async Task StopShard(string shardName, Exception? ex = null)
    {
        if (_agents.TryGetValue(shardName, out var agent))
        {
            if (agent.IsStopping)
            {
                return;
            }

            await agent.Stop(ex).ConfigureAwait(false);
            _agents.Remove(shardName);

            // Put some of this back?
            // throw new ShardStopException(agent.ShardName, e);
            // logger.LogError(exception, "Error when trying to stop projection shard '{ShardName}'",
            //     shardName);
        }
    }

    public async Task StopAll()
    {
        // This avoids issues around whether it was signaled here
        // first or through the coordinator first
        if (_isStopping)
        {
            return;
        }

        _isStopping = true;

        if (_coordinator != null)
        {
            await _coordinator.Stop().ConfigureAwait(false);
        }

#if NET8_0
        await _cancellation.CancelAsync().ConfigureAwait(false);
#else
        _cancellation.Cancel();
#endif
        await _highWater.Stop().ConfigureAwait(false);

        foreach (var agent in _agents.Values)
        {
            try
            {
                if (agent.IsStopping)
                {
                    continue;
                }

                await agent.Stop().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error trying to stop shard '{ShardName}@{DatabaseIdentifier}'",
                    agent.ShardName.Identity, Database.Identifier);
            }
        }

        _agents.Clear();

        // Need to restart this so that the daemon could
        // be restarted later
        _cancellation = new CancellationTokenSource();
        _highWater.ResetCancellation(_cancellation.Token);
    }

    public void Dispose()
    {
        _deadLetterBlock.SafeDispose();
        _coordinator?.Dispose();
        Tracker?.As<IDisposable>().Dispose();
        _cancellation?.Dispose();
        _highWater?.Dispose();
    }


    public Task RebuildProjection<TView>(CancellationToken token)
    {
        return RebuildProjection<TView>(5.Minutes(), token);
    }

    public Task RebuildProjection(Type projectionType, CancellationToken token)
    {
        return RebuildProjection(projectionType, 5.Minutes(), token);
    }

    public Task RebuildProjection(Type projectionType, TimeSpan shardTimeout, CancellationToken token)
    {
        if (projectionType.CanBeCastTo<IProjection>())
        {
            var projectionName = projectionType.FullNameInCode();
            return RebuildProjection(projectionName, shardTimeout, token);
        }

        if (projectionType.CanBeCastTo<IProjectionSource>())
        {
            try
            {
                var projection = Activator.CreateInstance(projectionType);
                if (projection is IProjectionSource wrapper)
                    return RebuildProjection(wrapper.ProjectionName, shardTimeout, token);

                throw new ArgumentOutOfRangeException(nameof(projectionType),
                    $"Type {projectionType.FullNameInCode()} is not a valid projection type");
            }
            catch (Exception e)
            {
                throw new ArgumentOutOfRangeException(nameof(projectionType),
                    $"No public default constructor for projection type {projectionType.FullNameInCode()}, you may need to supply the projection name instead");
            }
        }

        // Assume this is an aggregate type name
        return RebuildProjection(projectionType.NameInCode(), shardTimeout, token);
    }

    public Task RebuildProjection<TView>(TimeSpan shardTimeout, CancellationToken token)
    {
        if (typeof(TView).CanBeCastTo(typeof(ProjectionBase)) && typeof(TView).HasDefaultConstructor())
        {
            var projection = (ProjectionBase)Activator.CreateInstance(typeof(TView))!;
            return RebuildProjection(projection.ProjectionName, shardTimeout, token);
        }

        return RebuildProjection(typeof(TView).Name, shardTimeout, token);
    }

    public Task RebuildProjection(string projectionName, CancellationToken token)
    {
        return RebuildProjection(projectionName, 5.Minutes(), token);
    }

    public Task RebuildProjection(string projectionName, TimeSpan shardTimeout, CancellationToken token)
    {
        if (!_store.Options.Projections.TryFindProjection(projectionName, out var projection))
        {
            throw new ArgumentOutOfRangeException(nameof(projectionName),
                $"No registered projection matches the name '{projectionName}'. Available names are {_store.Options.Projections.AllProjectionNames().Join(", ")}");
        }

        return rebuildProjection(projection, shardTimeout, token);
    }

    public Task UseCoordinator(INodeCoordinator coordinator)
    {
        _coordinator = coordinator;
        return _coordinator.Start(this, _cancellation.Token);
    }

    public async Task StartShard(AsyncProjectionShard shard, ShardExecutionMode mode,
        CancellationToken cancellationToken)
    {
        if (!_highWater.IsRunning && mode == ShardExecutionMode.Continuous)
        {
            await StartDaemon().ConfigureAwait(false);
        }

        // Don't duplicate the shard
        if (_agents.ContainsKey(shard.Name.Identity))
        {
            return;
        }

        var agent = new ShardAgent(_store, shard, _logger, cancellationToken);
        var position = await agent.Start(this, mode).ConfigureAwait(false);

        Tracker.Publish(new ShardState(shard.Name, position) { Action = ShardAction.Started });

        _agents[shard.Name.Identity] = agent;

        // TODO -- put back
        // logger.LogError(ex, "Error when trying to start projection shard '{ShardName}'", shard.Name.Identity);
    }

    public ShardAgent[] CurrentShards()
    {
        return _agents.Values.ToArray();
    }


    private async Task rebuildProjection(IProjectionSource source, TimeSpan shardTimeout, CancellationToken token)
    {
        await Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        _logger.LogInformation("Starting to rebuild Projection {ProjectionName}@{DatabaseIdentifier}",
            source.ProjectionName, Database.Identifier);

        var running = _agents.Values.Where(x => x.ShardName.ProjectionName == source.ProjectionName).ToArray();
        foreach (var agent in running)
        {
            await agent.Stop().ConfigureAwait(false);
            _agents.Remove(agent.ShardName.Identity);
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        await _highWater.CheckNow().ConfigureAwait(false);

        // If there's no data, do nothing
        if (Tracker.HighWaterMark == 0)
        {
            _logger.LogInformation("Aborting projection rebuild because the high water mark is 0 (no event data)");
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        var shards = source.AsyncProjectionShards(_store);

        foreach (var shard in shards) Tracker.MarkAsRestarted(shard);

        // Teardown the current state
        await teardownExistingProjectionProgress(source, token, shards).ConfigureAwait(false);

        if (token.IsCancellationRequested)
        {
            return;
        }

        var mark = Tracker.HighWaterMark;

        // Is the shard count the optimal DoP here?
        await Parallel.ForEachAsync(shards,
            new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = shards.Count },
            async (shard, cancellationToken) =>
            {
                Tracker.MarkAsRestarted(shard);
                await StartShard(shard, ShardExecutionMode.Rebuild, cancellationToken).ConfigureAwait(false);
                await Tracker.WaitForShardState(shard.Name, mark, shardTimeout).ConfigureAwait(false);
            }).ConfigureAwait(false);

        foreach (var shard in shards)
        {
            if (_agents.TryGetValue(shard.Name.Identity, out var agent))
            {
                var serializationFailures = await agent.DrainSerializationFailureRecording().ConfigureAwait(false);
                if (serializationFailures > 0)
                {
                    Console.WriteLine(
                        $"There were {serializationFailures} deserialization failures during rebuild of shard {shard.Name.Identity}");
                }
            }

            await StopShard(shard.Name.Identity).ConfigureAwait(false);
        }
    }

    private async Task teardownExistingProjectionProgress(IProjectionSource source, CancellationToken token,
        IReadOnlyList<AsyncProjectionShard> shards)
    {
        var sessionOptions = SessionOptions.ForDatabase(Database);
        sessionOptions.AllowAnyTenant = true;
        await using var session = _store.LightweightSession(sessionOptions);
        source.Options.Teardown(session);

        foreach (var shard in shards)
            session.QueueOperation(new DeleteProjectionProgress(_store.Events, shard.Name.Identity));

        // Rewind previous DeadLetterEvents because you're going to replay them all anyway
        session.DeleteWhere<DeadLetterEvent>(x => x.ProjectionName == source.ProjectionName);

        await session.SaveChangesAsync(token).ConfigureAwait(false);
    }

    private static async Task waitForAllShardsToComplete(CancellationToken token, Task[] waiters)
    {
        var completion = Task.WhenAll(waiters);

        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (token.Register(state =>
                         {
                             ((TaskCompletionSource<object>)state!).TrySetResult(null!);
                         },
                         tcs))
        {
            var resultTask = await Task.WhenAny(completion, tcs.Task).ConfigureAwait(false);
            if (resultTask == tcs.Task)
            {
                // Operation cancelled
                throw new OperationCanceledException(token);
            }
        }
    }


    public AgentStatus StatusFor(string shardName)
    {
        return _agents.TryGetValue(shardName, out var agent)
            ? agent.Status
            : AgentStatus.Stopped;
    }

    public Task WaitForShardToStop(string shardName, TimeSpan? timeout = null)
    {
        if (StatusFor(shardName) == AgentStatus.Stopped)
        {
            return Task.CompletedTask;
        }

        bool IsStopped(ShardState s)
        {
            return s.ShardName.EqualsIgnoreCase(shardName) && s.Action == ShardAction.Stopped;
        }

        return Tracker.WaitForShardCondition(IsStopped, $"{shardName} is Stopped", timeout);
    }
}
