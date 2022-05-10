using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Baseline.Dates;
using Baseline.Reflection;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Daemon.Progress;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
#nullable enable
namespace Marten.Events.Daemon
{
    /// <summary>
    ///     The main class for running asynchronous projections
    /// </summary>
    internal class ProjectionDaemon: IProjectionDaemon
    {
        private readonly Dictionary<string, ShardAgent> _agents = new();
        private CancellationTokenSource _cancellation;
        private readonly HighWaterAgent _highWater;
        private readonly ILogger _logger;
        private readonly DocumentStore _store;
        private INodeCoordinator? _coordinator;

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
        }

        // Only for testing
        public ProjectionDaemon(DocumentStore store, ILogger logger) : this(store, store.Tenancy.Default.Database, new HighWaterDetector(new AutoOpenSingleQueryRunner(store.Tenancy.Default.Database), store.Events, logger), logger)
        {
        }

        public IMartenDatabase Database { get; }

        public Task UseCoordinator(INodeCoordinator coordinator)
        {
            _coordinator = coordinator;
            return _coordinator.Start(this, _cancellation.Token);
        }

        public DaemonSettings Settings { get; }

        public ShardStateTracker Tracker { get; }

        public bool IsRunning => _highWater.IsRunning;

        public async Task StartDaemon()
        {
            await Database.EnsureStorageExistsAsync(typeof(IEvent)).ConfigureAwait(false);
            await _highWater.Start().ConfigureAwait(false);
        }

        public async Task WaitForNonStaleData(TimeSpan timeout)
        {
            var stopWatch = Stopwatch.StartNew();
            var statistics = await Database.FetchEventStoreStatistics().ConfigureAwait(false);

            while (stopWatch.Elapsed < timeout)
            {
                if(CurrentShards().All(x => x.Position >= statistics.EventSequenceNumber))
                {
                    return;
                }
                await Task.Delay(100.Milliseconds()).ConfigureAwait(false);
            }

            var message = $"The active projection shards did not reach sequence {statistics.EventSequenceNumber} in time";
            throw new TimeoutException(message);
        }

        public async Task StartAllShards()
        {
            if (!_highWater.IsRunning)
            {
                await StartDaemon().ConfigureAwait(false);
            }

            var shards = _store.Options.Projections.AllShards();
            foreach (var shard in shards) await StartShard(shard, CancellationToken.None).ConfigureAwait(false);
        }

        public async Task StartShard(string shardName, CancellationToken token)
        {
            if (!_highWater.IsRunning)
            {
                await StartDaemon().ConfigureAwait(false);
            }

            // Latch it so it doesn't double start
            if (_agents.ContainsKey(shardName)) return;

            if (_store.Options.Projections.TryFindAsyncShard(shardName, out var shard)) await StartShard(shard, token).ConfigureAwait(false);
        }

        public async Task StartShard(AsyncProjectionShard shard, CancellationToken cancellationToken)
        {
            if (!_highWater.IsRunning)
            {
                await StartDaemon().ConfigureAwait(false);
            }

            // Don't duplicate the shard
            if (_agents.ContainsKey(shard.Name.Identity)) return;

            var parameters = new ActionParameters(async () =>
            {
                try
                {
                    var agent = new ShardAgent(_store, shard, _logger, cancellationToken);
                    var position = await agent.Start(this).ConfigureAwait(false);

                    Tracker.Publish(new ShardState(shard.Name, position) {Action = ShardAction.Started});

                    _agents[shard.Name.Identity] = agent;
                }
                catch (Exception e)
                {
                    throw new ShardStartException(shard, e);
                }
            }, cancellationToken);

            parameters.LogAction = (logger, ex) =>
            {
                logger.LogError(ex, "Error when trying to start projection shard '{ShardName}'", shard.Name.Identity);
            };

            await TryAction(parameters).ConfigureAwait(false);
        }

        public async Task StopShard(string shardName, Exception? ex = null)
        {
            if (_agents.TryGetValue(shardName, out var agent))
            {
                if (agent.IsStopping) return;

                var parameters = new ActionParameters(agent, async () =>
                {
                    try
                    {
                        await agent.Stop(ex).ConfigureAwait(false);
                        _agents.Remove(shardName);
                    }
                    catch (Exception e)
                    {
                        throw new ShardStopException(agent.ShardName, e);
                    }
                }, _cancellation.Token)
                {
                    LogAction = (logger, exception) =>
                    {
                        logger.LogError(exception, "Error when trying to stop projection shard '{ShardName}'",
                            shardName);
                    }
                };

                await TryAction(parameters).ConfigureAwait(false);
            }
        }

        private bool _isStopping = false;

        public async Task StopAll()
        {
            // This avoids issues around whether it was signaled here
            // first or through the coordinator first
            if (_isStopping) return;
            _isStopping = true;

            if (_coordinator != null)
            {
                await _coordinator.Stop().ConfigureAwait(false);
            }

            _cancellation.Cancel();
            await _highWater.Stop().ConfigureAwait(false);

            foreach (var agent in _agents.Values)
            {
                try
                {
                    if (agent.IsStopping) continue;

                    await agent.Stop().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error trying to stop shard '{ShardName}@{DatabaseIdentifier}'", agent.ShardName.Identity, Database.Identifier);
                }
            }

            _agents.Clear();

            // Need to restart this so that the daemon could
            // be restarted later
            _cancellation = new CancellationTokenSource();
        }

        public void Dispose()
        {
            _coordinator?.Dispose();
            Tracker?.As<IDisposable>().Dispose();
            _cancellation?.Dispose();
            _highWater?.Dispose();
        }


        public Task RebuildProjection<TView>(CancellationToken token)
        {
            if (typeof(TView).CanBeCastTo(typeof(ProjectionBase)) && typeof(TView).HasDefaultConstructor())
            {
                var projection = (ProjectionBase)Activator.CreateInstance(typeof(TView))!;
                return RebuildProjection(projection.ProjectionName, token);
            }

            return RebuildProjection(typeof(TView).Name, token);
        }

        public Task RebuildProjection(string projectionName, CancellationToken token)
        {
            if (!_store.Options.Projections.TryFindProjection(projectionName, out var projection))
                throw new ArgumentOutOfRangeException(nameof(projectionName),
                    $"No registered projection matches the name '{projectionName}'. Available names are {_store.Options.Projections.AllProjectionNames().Join(", ")}");

            return rebuildProjection(projection, token);
        }

        public ShardAgent[] CurrentShards()
        {
            return _agents.Values.ToArray();
        }


        private async Task rebuildProjection(IProjectionSource source, CancellationToken token)
        {
            _logger.LogInformation("Starting to rebuild Projection {ProjectionName}@{DatabaseIdentifier}", source.ProjectionName, Database.Identifier);

            var running = _agents.Values.Where(x => x.ShardName.ProjectionName == source.ProjectionName).ToArray();
            foreach (var agent in running)
            {
                await agent.Stop().ConfigureAwait(false);
                _agents.Remove(agent.ShardName.Identity);
            }

            if (token.IsCancellationRequested) return;

            // If there's no data, do nothing
            while (Tracker.HighWaterMark == 0)
            {
                return;
            }

            if (token.IsCancellationRequested) return;

            var shards = source.AsyncProjectionShards(_store);

            foreach (var shard in shards)
            {
                Tracker.MarkAsRestarted(shard);
            }

            // Teardown the current state
            var session = await _store.OpenSessionAsync(new SessionOptions{AllowAnyTenant = true, Tracking = DocumentTracking.None}, token).ConfigureAwait(false);
            await using (session.ConfigureAwait(false))
            {
                source.Options.Teardown(session);

                foreach (var shard in shards)
                {
                    session.QueueOperation(new DeleteProjectionProgress(_store.Events, shard.Name.Identity));
                }

                await session.SaveChangesAsync(token).ConfigureAwait(false);
            }

            if (token.IsCancellationRequested) return;

            var mark = Tracker.HighWaterMark;

#if NET6_0_OR_GREATER
            // Is the shard count the optimal DoP here?
            await Parallel.ForEachAsync(shards, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = shards.Count },
                async (shard, cancellationToken) =>
            {
                Tracker.MarkAsRestarted(shard);
                await StartShard(shard, cancellationToken).ConfigureAwait(false);
                await Tracker.WaitForShardState(shard.Name, mark, 5.Minutes()).ConfigureAwait(false);
            }).ConfigureAwait(false);
#else

            var waiters = shards.Select(async x =>
            {
                Tracker.MarkAsRestarted(x);
                await StartShard(x, token).ConfigureAwait(false);
                await Tracker.WaitForShardState(x.Name, mark, 5.Minutes()).ConfigureAwait(false);
            }).ToArray();

            await waitForAllShardsToComplete(token, waiters).ConfigureAwait(false);
#endif
            foreach (var shard in shards) await StopShard(shard.Name.Identity).ConfigureAwait(false);
        }

        private static async Task waitForAllShardsToComplete(CancellationToken token, Task[] waiters)
        {
            var completion = Task.WhenAll(waiters);

            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(state =>
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


        internal async Task TryAction(ActionParameters parameters)
        {
            if (parameters.Delay != default) await Task.Delay(parameters.Delay, parameters.Cancellation).ConfigureAwait(false);

            if (parameters.Cancellation.IsCancellationRequested) return;

            try
            {
                await parameters.Action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Get out of here and do nothing if this is just a result of a Task being
                // cancelled
                if (ex is TaskCanceledException)
                {
                    // Unless this is a parent action of a group action that failed and bailed out w/ a
                    // TaskCanceledException that is
                    if (parameters.Group?.Exception is ApplyEventException apply && parameters.GroupActionMode == GroupActionMode.Parent)
                    {
                        ex = apply;
                    }
                    else
                    {
                        return;
                    }
                }

                // IF you're using a group, you're using the group's cancellation, and it's going to be
                // cancelled already
                if (parameters.Group == null && parameters.Cancellation.IsCancellationRequested) return;
                parameters.Group?.Abort(ex);

                parameters.LogAction(_logger, ex);

                var continuation = Settings.DetermineContinuation(ex, parameters.Attempts);
                switch (continuation)
                {
                    case RetryLater r:
                        parameters.IncrementAttempts(r.Delay);
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.LogDebug("Retrying in {Milliseconds} @{DatabaseIdentifier}", r.Delay.TotalMilliseconds, Database.Identifier);
                        }
                        await TryAction(parameters).ConfigureAwait(false);
                        break;
                    case Resiliency.StopShard:
                        if (parameters.Shard != null) await StopShard(parameters.Shard.ShardName.Identity, ex).ConfigureAwait(false);
                        break;
                    case StopAllShards:
#if NET6_0_OR_GREATER
                        await Parallel.ForEachAsync(_agents.Keys.ToArray(), _cancellation.Token,
                            async (name, _) => await StopShard(name, ex).ConfigureAwait(false)
                        ).ConfigureAwait(false);
#else
                        var tasks = _agents.Keys.ToArray().Select(name =>
                        {
                            return Task.Run(async () =>
                            {
                                await StopShard(name, ex).ConfigureAwait(false);
                            }, _cancellation.Token);
                        });

                        await Task.WhenAll(tasks).ConfigureAwait(false);
#endif

                        Tracker.Publish(
                            new ShardState(ShardName.All, Tracker.HighWaterMark)
                            {
                                Action = ShardAction.Stopped, Exception = ex
                            });
                        break;
                    case PauseShard pause:
                        if (parameters.Shard != null) await parameters.Shard.Pause(pause.Delay).ConfigureAwait(false);
                        break;
                    case PauseAllShards pauseAll:
#if NET6_0_OR_GREATER
                        await Parallel.ForEachAsync(_agents.Values.ToArray(), _cancellation.Token,
                                async (agent, _) => await agent.Pause(pauseAll.Delay).ConfigureAwait(false))
                            .ConfigureAwait(false);
#else
                        var tasks2 = _agents.Values.ToArray().Select(agent =>
                        {
                            return Task.Run(async () =>
                            {
                                await agent.Pause(pauseAll.Delay).ConfigureAwait(false);
                            }, _cancellation.Token);
                        });

                        await Task.WhenAll(tasks2).ConfigureAwait(false);
#endif
                        break;

                    case SkipEvent skip:
                        if (parameters.GroupActionMode == GroupActionMode.Child)
                        {
                            // Don't do anything, this has to be retried from the parent
                            // task
                            return;
                        }

                        await parameters.ApplySkipAsync(skip, Database).ConfigureAwait(false);

                        _logger.LogInformation("Skipping event #{Sequence} ({EventType}@{DatabaseIdentifier}) in shard '{ShardName}'",
                            skip.Event.Sequence, skip.Event.EventType.GetFullName(), parameters.Shard.Name, Database.Identifier);
                        await WriteSkippedEvent(skip.Event, parameters.Shard.Name, (ex as ApplyEventException)!).ConfigureAwait(false);

                        await TryAction(parameters).ConfigureAwait(false);
                        break;

                    case DoNothing:
                        // Don't do anything.
                        break;
                }
            }
        }

        internal async Task WriteSkippedEvent(IEvent @event, ShardName shardName, ApplyEventException exception)
        {
            try
            {
                var deadLetterEvent = new DeadLetterEvent(@event, shardName, exception);
                var session =
                    _store.OpenSession(SessionOptions.ForDatabase(Database));

                await using (session.ConfigureAwait(false))
                {
                    session.Store(deadLetterEvent);
                    await session.SaveChangesAsync().ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to write dead letter event {Event} to shard {ShardName}@{DatabaseIdentifier}", @event,
                    shardName, Database.Identifier);
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
            if (StatusFor(shardName) == AgentStatus.Stopped) return Task.CompletedTask;

            bool IsStopped(ShardState s)
            {
                return s.ShardName.EqualsIgnoreCase(shardName) && s.Action == ShardAction.Stopped;
            }

            return Tracker.WaitForShardCondition(IsStopped, $"{shardName} is Stopped", timeout);
        }
    }
}
