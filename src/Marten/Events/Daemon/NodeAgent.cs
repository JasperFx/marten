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
    public class NodeAgent : IDisposable
    {
        private readonly DocumentStore _store;
        private readonly ILogger<IProjection> _logger;
        private readonly Dictionary<string, ProjectionAgent> _agents = new Dictionary<string, ProjectionAgent>();
        private readonly CancellationTokenSource _cancellation;
        private readonly HighWaterAgent _highWater;

        public NodeAgent(DocumentStore store, ILogger<IProjection> logger)
        {
            _cancellation = new CancellationTokenSource();
            _store = store;
            _logger = logger;
            var detector = new HighWaterDetector(store.Tenancy.Default, store.Events);

            Tracker = new ShardStateTracker();
            _highWater = new HighWaterAgent(detector, Tracker, logger, store.Events.Daemon, _cancellation.Token);

        }

        public ShardStateTracker Tracker { get; }

        public void Start()
        {
            _store.Tenancy.Default.EnsureStorageExists(typeof(IEvent));
            _highWater.Start();
        }

        public async Task StartShard(string shardName)
        {
            if (_store.Events.Projections.TryFindAsyncShard(shardName, out var shard))
            {
                // TODO -- log the start, or error if it fails
                var agent = new ProjectionAgent(_store, shard, _logger);
                await agent.Start(Tracker);

                _agents[shardName] = agent;
            }
        }

        public async Task StopShard(string shardName)
        {
            if (_agents.TryGetValue(shardName, out var agent))
            {
                await agent.Stop();
                _agents.Remove(shardName);
            }
        }

        public async Task StopAll()
        {
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
