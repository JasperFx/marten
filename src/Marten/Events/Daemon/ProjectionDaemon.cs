using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Baseline.Dates;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Daemon.Progress;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{
    /// <summary>
    /// The main class for running asynchronous projections
    /// </summary>
    internal class ProjectionDaemon : IProjectionDaemon
    {
        private readonly DocumentStore _store;
        private readonly ILogger _logger;
        private readonly Dictionary<string, ShardAgent> _agents = new Dictionary<string, ShardAgent>();
        private readonly CancellationTokenSource _cancellation;
        private readonly HighWaterAgent _highWater;
        private bool _hasStarted;

        public ProjectionDaemon(DocumentStore store, ILogger logger)
        {
            _cancellation = new CancellationTokenSource();
            _store = store;
            _logger = logger;
            var detector = new HighWaterDetector(store.Tenancy.Default, store.Events);

            Tracker = new ShardStateTracker(logger);
            _highWater = new HighWaterAgent(detector, Tracker, logger, store.Events.Daemon, _cancellation.Token);

            Settings = store.Events.Daemon;
        }

        public DaemonSettings Settings { get; }

        public ShardStateTracker Tracker { get; }

        public async Task StartDaemon()
        {
            _store.Tenancy.Default.EnsureStorageExists(typeof(IEvent));
            await _highWater.Start();
            _hasStarted = true;
        }

        public async Task StartAll()
        {
            if (!_hasStarted)
            {
                await StartDaemon();
            }

            var shards = _store.Events.Projections.AllShards();
            foreach (var shard in shards)
            {
                await StartShard(shard, CancellationToken.None);
            }
        }

        public async Task StartShard(string shardName, CancellationToken token)
        {
            // Latch it so it doesn't double start
            if (_agents.ContainsKey(shardName)) return;

            if (_store.Events.Projections.TryFindAsyncShard(shardName, out var shard))
            {
                await StartShard(shard, token);
            }
        }

        public async Task StartShard(AsyncProjectionShard shard, CancellationToken cancellationToken)
        {
            if (!_hasStarted)
            {
                await StartDaemon();
            }

            // Don't duplicate the shard
            if (_agents.ContainsKey(shard.Name.Identity)) return;

            await TryAction(null, async () =>
            {
                try
                {
                    var agent = new ShardAgent(_store, shard, _logger, cancellationToken);
                    var position = await agent.Start(this);

                    Tracker.Publish(new ShardState(shard.Name, position){Action = ShardAction.Started});

                    _agents[shard.Name.Identity] = agent;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error when trying to start projection shard '{ShardName}'", shard.Name.Identity);
                    throw new ShardStartException(shard.Name, e);
                }
            }, cancellationToken);
        }

        public async Task StopShard(string shardName, Exception ex = null)
        {
            if (_agents.TryGetValue(shardName, out var agent))
            {
                if (agent.IsStopping()) return;

                await TryAction(agent, async () =>
                {
                    try
                    {
                        await agent.Stop(ex);
                        _agents.Remove(shardName);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Error when trying to stop projection shard '{ShardName}'", shardName);
                        throw new ShardStopException(agent.ShardName, e);
                    }
                }, _cancellation.Token);
            }
        }

        public async Task StopAll()
        {
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


        }

        public void Dispose()
        {
            Tracker?.As<IDisposable>().Dispose();
            _cancellation?.Dispose();
            _highWater?.Dispose();
        }


        public Task RebuildProjection<TView>(CancellationToken token) => RebuildProjection(typeof(TView).Name, token);

        public Task RebuildProjection(string projectionName, CancellationToken token)
        {
            if (!_store.Events.Projections.TryFindProjection(projectionName, out var projection))
            {
                throw new ArgumentOutOfRangeException(nameof(projectionName),
                    $"No registered projection matches the name '{projectionName}'. Available names are {_store.Events.Projections.AllProjectionNames().Join(", ")}");
            }

            return RebuildProjection(projection, token);

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

            if (Tracker.HighWaterMark == 0)
            {
                await _highWater.CheckNow();
            }

            if (token.IsCancellationRequested) return;

            var shards = source.AsyncProjectionShards(_store);

            // Teardown the current state
            await using (var session = _store.LightweightSession())
            {
                source.Options.Teardown(session);

                foreach (var shard in shards)
                {
                    session.QueueOperation(new DeleteProjectionProgress(_store.Events, shard.Name.Identity));
                }

                await session.SaveChangesAsync(token);
            }

            if (token.IsCancellationRequested) return;


            var waiters = shards.Select(async x =>
            {
                await StartShard(x, token);
                return Tracker.WaitForShardState(x.Name, Tracker.HighWaterMark, 5.Minutes());
            }).Select(x => x.Unwrap()).ToArray();

            await waitForAllShardsToComplete(token, waiters);

            foreach (var shard in shards)
            {
                await StopShard(shard.Name.Identity);
            }
        }

        private static async Task waitForAllShardsToComplete(CancellationToken token, Task[] waiters)
        {
            var completion = Task.WhenAll(waiters);
            var tcs = new TaskCompletionSource<bool>();
            // ReSharper disable once MethodSupportsCancellation
            token.Register(() => tcs.SetCanceled());

            await Task.WhenAny(tcs.Task, completion);
        }


        internal async Task TryAction(ShardAgent shard, Func<Task> action, CancellationToken token, int attempts = 0, TimeSpan delay = default)
        {
            if (delay != default)
            {
                await Task.Delay(delay, token);
            }

            if (token.IsCancellationRequested) return;

            try
            {
                await action();
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) return;

                _logger.LogError(ex, "Error in Async Projection '{ShardName}' / '{Message}'", shard.ShardName.Identity, ex.Message);

                var continuation = Settings.DetermineContinuation(ex, attempts);
                switch (continuation)
                {
                    case RetryLater r:
                        await TryAction(shard, action, token, attempts + 1, r.Delay);
                        break;
                    case StopProjection:
                        if (shard != null) await StopShard(shard.ShardName.Identity, ex);
                        break;
                    case StopAllProjections:
                        var tasks = _agents.Keys.ToArray().Select(name =>
                        {
                            return Task.Run(async () =>
                            {
                                await StopShard(name, ex);
                            }, _cancellation.Token);
                        });

                        await Task.WhenAll(tasks);

                        Tracker.Publish(new ShardState(ShardName.All, Tracker.HighWaterMark){Action = ShardAction.Stopped, Exception = ex});
                        break;
                    case PauseProjection pause:
                        if (shard != null)
                        {
                            await shard.Pause(pause.Delay);
                        }
                        break;
                    case PauseAllProjections pauseAll:
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

                    case DoNothing:
                        // Don't do anything.
                        break;
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
            if (StatusFor(shardName) == AgentStatus.Stopped) return Task.CompletedTask;

            bool IsStopped(ShardState s) => s.ShardName.EqualsIgnoreCase(shardName) && s.Action == ShardAction.Stopped;

            return Tracker.WaitForShardCondition(IsStopped, $"{shardName} is Stopped", timeout);
        }
    }
}
