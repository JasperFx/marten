using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Baseline.Dates;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Daemon.Progress;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Services;
using Microsoft.Extensions.Logging;

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
        private INodeCoordinator _coordinator;

        public ProjectionDaemon(DocumentStore store, IHighWaterDetector detector, ILogger logger)
        {
            _cancellation = new CancellationTokenSource();
            _store = store;
            _logger = logger;

            Tracker = new ShardStateTracker(logger);
            _highWater = new HighWaterAgent(detector, Tracker, logger, store.Events.Daemon, _cancellation.Token);

            Settings = store.Events.Daemon;
        }

        public ProjectionDaemon(DocumentStore store, ILogger logger) : this(store, new HighWaterDetector(new AutoOpenSingleQueryRunner(store.Tenancy.Default), store.Events), logger)
        {
        }

        public Task UseCoordinator(INodeCoordinator coordinator)
        {
            _coordinator = coordinator;
            return _coordinator.Start(this, _cancellation.Token);
        }

        public DaemonSettings Settings { get; }

        public ShardStateTracker Tracker { get; }

        public bool IsRunning => _highWater.IsRunning;

        public async Task StartHighWaterDetection()
        {
            _store.Tenancy.Default.EnsureStorageExists(typeof(IEvent));
            await _highWater.Start();
        }

        public Task WaitForNonStaleData(TimeSpan timeout)
        {
            var completion = new TaskCompletionSource<bool>();
            var timeoutCancellation = new CancellationTokenSource(timeout);


            Task.Run(async () =>
            {
                var statistics = await _store.Advanced.FetchEventStoreStatistics(timeoutCancellation.Token);
                timeoutCancellation.Token.Register(() =>
                {
                    completion.TrySetException(new TimeoutException(
                        $"The active projection shards did not reach sequence {statistics.EventSequenceNumber} in time"));
                });

                if (CurrentShards().All(x => x.Position >= statistics.EventSequenceNumber))
                {
                    completion.SetResult(true);
                    return;
                }

                while (!timeoutCancellation.IsCancellationRequested)
                {
                    await Task.Delay(100.Milliseconds(), timeoutCancellation.Token);

                    if (CurrentShards().All(x => x.Position >= statistics.EventSequenceNumber))
                    {
                        completion.SetResult(true);
                        return;
                    }
                }
            }, timeoutCancellation.Token);

            return completion.Task;

        }

        public async Task StartAllShards()
        {
            if (!_highWater.IsRunning)
            {
                await StartHighWaterDetection();
            }

            var shards = _store.Events.Projections.AllShards();
            foreach (var shard in shards) await StartShard(shard, CancellationToken.None);
        }

        public async Task StartShard(string shardName, CancellationToken token)
        {
            if (!_highWater.IsRunning)
            {
                await StartHighWaterDetection();
            }

            // Latch it so it doesn't double start
            if (_agents.ContainsKey(shardName)) return;

            if (_store.Events.Projections.TryFindAsyncShard(shardName, out var shard)) await StartShard(shard, token);
        }

        public async Task StartShard(AsyncProjectionShard shard, CancellationToken cancellationToken)
        {
            // Don't duplicate the shard
            if (_agents.ContainsKey(shard.Name.Identity)) return;

            var parameters = new ActionParameters(async () =>
            {
                try
                {
                    var agent = new ShardAgent(_store, shard, _logger, cancellationToken);
                    var position = await agent.Start(this);

                    Tracker.Publish(new ShardState(shard.Name, position) {Action = ShardAction.Started});

                    _agents[shard.Name.Identity] = agent;
                }
                catch (Exception e)
                {
                    throw new ShardStartException(shard.Name, e);
                }
            }, cancellationToken);

            parameters.LogAction = (logger, ex) =>
            {
                logger.LogError(ex, "Error when trying to start projection shard '{ShardName}'", shard.Name.Identity);
            };

            await TryAction(parameters);
        }

        public async Task StopShard(string shardName, Exception ex = null)
        {
            if (_agents.TryGetValue(shardName, out var agent))
            {
                if (agent.IsStopping()) return;

                var parameters = new ActionParameters(agent, async () =>
                {
                    try
                    {
                        await agent.Stop(ex);
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

                await TryAction(parameters);
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
                await _coordinator.Stop();
            }

            _cancellation.Cancel();
            await _highWater.Stop();

            foreach (var agent in _agents.Values)
            {
                try
                {
                    if (agent.IsStopping()) continue;

                    await agent.Stop();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error trying to stop shard '{ShardName}'", agent.ShardName.Identity);
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
            return RebuildProjection(typeof(TView).Name, token);
        }

        public Task RebuildProjection(string projectionName, CancellationToken token)
        {
            if (!_store.Events.Projections.TryFindProjection(projectionName, out var projection))
                throw new ArgumentOutOfRangeException(nameof(projectionName),
                    $"No registered projection matches the name '{projectionName}'. Available names are {_store.Events.Projections.AllProjectionNames().Join(", ")}");

            return RebuildProjection(projection, token);
        }

        public ShardAgent[] CurrentShards()
        {
            return _agents.Values.ToArray();
        }


        private async Task RebuildProjection(ProjectionSource source, CancellationToken token)
        {
            _logger.LogInformation("Starting to rebuild Projection {ProjectionName}", source.ProjectionName);

            var running = _agents.Values.Where(x => x.ShardName.ProjectionName == source.ProjectionName).ToArray();
            foreach (var agent in running)
            {
                await agent.Stop();
                _agents.Remove(agent.ShardName.Identity);
            }

            if (token.IsCancellationRequested) return;

            if (Tracker.HighWaterMark == 0) await _highWater.CheckNow();

            if (token.IsCancellationRequested) return;

            var shards = source.AsyncProjectionShards(_store);

            // Teardown the current state
            await using (var session = _store.LightweightSession())
            {
                source.Options.Teardown(session);

                foreach (var shard in shards)
                    session.QueueOperation(new DeleteProjectionProgress(_store.Events, shard.Name.Identity));

                await session.SaveChangesAsync(token);
            }

            if (token.IsCancellationRequested) return;


            var waiters = shards.Select(async x =>
            {
                await StartShard(x, token);
                return Tracker.WaitForShardState(x.Name, Tracker.HighWaterMark, 5.Minutes());
            }).Select(x => x.Unwrap()).ToArray();

            await waitForAllShardsToComplete(token, waiters);

            foreach (var shard in shards) await StopShard(shard.Name.Identity);
        }

        private static async Task waitForAllShardsToComplete(CancellationToken token, Task[] waiters)
        {
            var completion = Task.WhenAll(waiters);
            var tcs = new TaskCompletionSource<bool>();
            // ReSharper disable once MethodSupportsCancellation
            token.Register(() => tcs.SetCanceled());

            await Task.WhenAny(tcs.Task, completion);
        }


        internal async Task TryAction(ActionParameters parameters)
        {
            if (parameters.Delay != default) await Task.Delay(parameters.Delay, parameters.Cancellation);

            if (parameters.Cancellation.IsCancellationRequested) return;

            try
            {
                await parameters.Action();
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
                            _logger.LogDebug("Retrying in {Milliseconds}", r.Delay.TotalMilliseconds);
                        }
                        await TryAction(parameters);
                        break;
                    case Resiliency.StopShard:
                        if (parameters.Shard != null) await StopShard(parameters.Shard.ShardName.Identity, ex);
                        break;
                    case StopAllShards:
                        var tasks = _agents.Keys.ToArray().Select(name =>
                        {
                            return Task.Run(async () =>
                            {
                                await StopShard(name, ex);
                            }, _cancellation.Token);
                        });

                        await Task.WhenAll(tasks);

                        Tracker.Publish(
                            new ShardState(ShardName.All, Tracker.HighWaterMark)
                            {
                                Action = ShardAction.Stopped, Exception = ex
                            });
                        break;
                    case PauseShard pause:
                        if (parameters.Shard != null) await parameters.Shard.Pause(pause.Delay);
                        break;
                    case PauseAllShards pauseAll:
                        var tasks2 = _agents.Values.ToArray().Select(agent =>
                        {
                            return Task.Run(async () =>
                            {
                                await agent.Pause(pauseAll.Delay);
                            }, _cancellation.Token);
                        });

                        await Task.WhenAll(tasks2);
                        break;

                    case SkipEvent skip:
                        if (parameters.GroupActionMode == GroupActionMode.Child)
                        {
                            // Don't do anything, this has to be retried from the parent
                            // task
                            return;
                        }

                        parameters.ApplySkip(skip);

                        _logger.LogInformation("Skipping event #{Sequence} ({EventType}) in shard '{ShardName}'",
                            skip.Event.Sequence, skip.Event.EventType.GetFullName(), parameters.Shard.Name);
                        await WriteSkippedEvent(skip.Event, parameters.Shard.Name, ex as ApplyEventException);

                        await TryAction(parameters);
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
                using (var session = _store.LightweightSession())
                {
                    session.Store(deadLetterEvent);
                    await session.SaveChangesAsync();
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to write dead letter event {Event} to shard {ShardName}", @event,
                    shardName);
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
