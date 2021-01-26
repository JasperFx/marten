using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Projections;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{
    // TODO -- implement IAsyncDisposable
    public class NodeAgent : INodeAgent, IDisposable
    {
        private readonly DocumentStore _store;
        private readonly ILogger<IProjection> _logger;
        private readonly Dictionary<string, ProjectionAgent> _agents = new Dictionary<string, ProjectionAgent>();
        private readonly CancellationTokenSource _cancellation;
        private readonly HighWaterAgent _highWater;
        private bool _hasStarted;
        private INodeCoordinator _coordinator;

        // ReSharper disable once ContextualLoggerProblem
        public NodeAgent(DocumentStore store, ILogger<IProjection> logger)
        {
            _cancellation = new CancellationTokenSource();
            _store = store;
            _logger = logger;
            var detector = new HighWaterDetector(store.Tenancy.Default, store.Events);

            Tracker = new ShardStateTracker();
            _highWater = new HighWaterAgent(detector, Tracker, logger, store.Events.Daemon, _cancellation.Token);

        }

        public async Task RegisterCoordinator(INodeCoordinator coordinator)
        {
            await coordinator.Start(this, _cancellation.Token);
            _coordinator = coordinator;
        }

        public ShardStateTracker Tracker { get; }

        // TODO -- only start the high water when there's anything to start!
        public void StartNode()
        {
            _store.Tenancy.Default.EnsureStorageExists(typeof(IEvent));
            _highWater.Start();
            _hasStarted = true;
        }

        public async Task StartAll()
        {
            if (!_hasStarted) StartNode();
            var shards = _store.Events.Projections.AllShards();
            foreach (var shard in shards)
            {
                await StartShard(shard);
            }

        }

        public async Task StartShard(string shardName)
        {

            if (_store.Events.Projections.TryFindAsyncShard(shardName, out var shard))
            {
                await StartShard(shard);
            }
        }

        public async Task StartShard(IAsyncProjectionShard shard)
        {
            if (!_hasStarted) StartNode();

            // TODO -- log the start, or error if it fails
            var agent = new ProjectionAgent(_store, shard, _logger);
            var position = await agent.Start(Tracker);

            Tracker.Publish(new ShardState(shard.ProjectionOrShardName, position){Action = ShardAction.Started});

            _agents[shard.ProjectionOrShardName] = agent;


        }

        // TODO -- if all the shards are stopped, stop the high water agent
        public async Task StopShard(string shardName)
        {
            if (_agents.TryGetValue(shardName, out var agent))
            {
                await agent.Stop();
                _agents.Remove(shardName);

                Tracker.Publish(new ShardState(shardName, agent.Position){Action = ShardAction.Stopped});

            }
        }

        public async Task StopAll()
        {
            // TODO -- stop the high water checking??
            foreach (var agent in _agents.Values)
            {
                await agent.Stop();
            }

            _agents.Clear();


        }

        public void Dispose()
        {
            Tracker?.Dispose();
            _cancellation?.Dispose();
            _highWater?.Dispose();
        }


    }
}
