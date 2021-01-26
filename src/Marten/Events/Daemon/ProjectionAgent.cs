using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon
{

    internal interface IProjectionAgent
    {
        Task Stop();

        Task TryRestart();

        Task<long> Start(ShardStateTracker tracker);
        string ProjectionOrShardName { get; }
    }

    // TODO -- need a Drain() method
    // TODO -- need a Dispose that really cleans things off. May need/want IAsyncDisposable
    internal class ProjectionAgent : IProjectionUpdater, IObserver<ShardState>, IProjectionAgent
    {
        private readonly DocumentStore _store;
        private readonly IAsyncProjectionShard _projectionShard;
        private readonly ILogger<IProjection> _logger;
        private ITargetBlock<EventRange> _hopper;
        private readonly ProjectionController _controller;
        private readonly ActionBlock<Command> _commandBlock;
        private readonly TransformBlock<EventRange, EventRange> _loader;
        private EventFetcher _fetcher;
        private ShardStateTracker _tracker;
        private IDisposable _subscription;
        private readonly CancellationTokenSource _cancellationSource = new CancellationTokenSource();

        // ReSharper disable once ContextualLoggerProblem
        public ProjectionAgent(DocumentStore store, IAsyncProjectionShard projectionShard, ILogger<IProjection> logger)
        {

            _store = store;
            _projectionShard = projectionShard;
            _logger = logger;

            var singleFile = new ExecutionDataflowBlockOptions
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1,
                CancellationToken = _cancellationSource.Token
            };

            _commandBlock = new ActionBlock<Command>(processCommand, singleFile);

            _controller =
                new ProjectionController(projectionShard.ProjectionOrShardName, this, projectionShard.Options);

            _loader = new TransformBlock<EventRange, EventRange>(loadEvents, singleFile);
        }

        // TODO -- use IAsyncDisposable
        public async Task Stop()
        {
            _cancellationSource.Cancel();
            _commandBlock.Complete();
            await _commandBlock.Completion;

            _loader.Complete();
            await _loader.Completion;

            _hopper.Complete();
            await _hopper.Completion;

            await _projectionShard.Stop();

            _subscription.Dispose();

        }

        private async Task<EventRange> loadEvents(EventRange range)
        {
            try
            {
                // TODO -- resiliency here.
                await _fetcher.Load(range, _cancellationSource.Token);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error loading events for " + range);
                // TODO -- retry? circuit breaker?
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug($"Loaded events for {range}");
            }

            return range;
        }

        private void processCommand(Command command) => command.Apply(_controller);

        public AgentStatus Status { get; private set; }


        public void StartRange(EventRange range)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Enqueued processing of " + range);
            }

            _loader.Post(range);
        }

        public async Task<long> Start(ShardStateTracker tracker)
        {
            _logger.LogInformation($"Starting projection agent for '{_projectionShard.ProjectionOrShardName}'");

            _tracker = tracker;


            _fetcher = new EventFetcher(_store, _projectionShard.EventFilters);
            _hopper = _projectionShard.Start(this, _logger, _cancellationSource.Token);
            _loader.LinkTo(_hopper);

            var lastCommitted = await _store.Events.ProjectionProgressFor(_projectionShard.ProjectionOrShardName);

            _commandBlock.Post(Command.Started(tracker.HighWaterMark, lastCommitted));

            _subscription = _tracker.Subscribe(this);

            _logger.LogInformation($"Projection agent for '{_projectionShard.ProjectionOrShardName}' has started from sequence {lastCommitted} and a high water mark of {tracker.HighWaterMark}");

            Status = AgentStatus.Running;

            Position = lastCommitted;
            return lastCommitted;
        }

        public Task TryRestart()
        {
            throw new NotImplementedException();
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
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug($"Projection Shard '{ProjectionOrShardName}' received high water mark at {value.Sequence}");
                }

                _commandBlock.Post(
                    Command.HighWaterMarkUpdated(value.Sequence));
            }
        }

        public string ProjectionOrShardName => _projectionShard.ProjectionOrShardName;

        public ProjectionUpdateBatch StartNewBatch(EventRange range)
        {
            var session = _store.LightweightSession();
            return new ProjectionUpdateBatch(_store.Events, (DocumentSessionBase) session, range);
        }

        public async Task ExecuteBatch(ProjectionUpdateBatch batch)
        {
            await batch.Queue.Completion;

            using (var session = (DocumentSessionBase)_store.LightweightSession())
            {
                try
                {
                    await session.ExecuteBatchAsync(batch, _cancellationSource.Token);

                    _logger.LogInformation($"Shard '{ProjectionOrShardName}': Executed updates for {batch.Range}");
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"Failure in shard '{ProjectionOrShardName}' trying to execute an update batch for {batch.Range}");
                    // TODO -- error handling

                    throw;
                }
            }

            batch.Dispose();


            Position = batch.Range.SequenceCeiling;


            _tracker.Publish(new ShardState(ProjectionOrShardName, batch.Range.SequenceCeiling));

            _commandBlock.Post(Command.Completed(batch.Range));
        }

        public long Position { get; set; }
    }
}
