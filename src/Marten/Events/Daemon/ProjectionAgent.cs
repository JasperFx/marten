using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Internal.Sessions;

namespace Marten.Events.Daemon
{

    // TODO -- need a Drain() method
    // TODO -- need a Dispose that really cleans things off
    internal class ProjectionAgent : IProjectionUpdater, IObserver<ShardState>
    {
        private readonly DocumentStore _store;
        private readonly IAsyncProjectionShard _projectionShard;
        private ITargetBlock<EventRange> _hopper;
        private readonly ProjectionController _controller;
        private readonly ActionBlock<Command> _commandBlock;
        private readonly TransformBlock<EventRange, EventRange> _loader;
        private EventFetcher _fetcher;
        private ShardStateTracker _tracker;
        private IDisposable _subscription;

        public ProjectionAgent(DocumentStore store, IAsyncProjectionShard projectionShard)
        {
            _store = store;
            _projectionShard = projectionShard;

            var singleFile = new ExecutionDataflowBlockOptions
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            };

            _commandBlock = new ActionBlock<Command>(processCommand, singleFile);

            _controller =
                new ProjectionController(projectionShard.ProjectionOrShardName, this, projectionShard.Options);

            _loader = new TransformBlock<EventRange, EventRange>(loadEvents, singleFile);
        }

        private async Task<EventRange> loadEvents(EventRange range)
        {
            // TODO -- pass around a real CancellationToken
            await _fetcher.Load(range, CancellationToken.None);

            return range;
        }

        private void processCommand(Command command) => command.Apply(_controller);

        public AgentStatus Status { get; private set; }


        public void StartRange(EventRange range) => _loader.Post(range);

        public async Task Start(ShardStateTracker tracker)
        {
            _tracker = tracker;


            _fetcher = new EventFetcher(_store, _projectionShard.EventFilters);
            _hopper = _projectionShard.Start(this);
            _loader.LinkTo(_hopper);

            var lastCommitted = await _store.Events.ProjectionProgressFor(_projectionShard.ProjectionOrShardName);

            _commandBlock.Post(Command.Started(tracker.HighWaterMark, lastCommitted));

            // TODO -- track and dispose this
            _subscription = _tracker.Subscribe(this);
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
                    // TODO -- use a real cancellation
                    await session.ExecuteBatchAsync(batch, CancellationToken.None);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                    throw;
                }
            }

            batch.Dispose();



            // TODO -- error handling
            // TODO -- instrumentation
            // TODO -- re-evaluate what should be happening next

            _tracker.Publish(new ShardState(ProjectionOrShardName, batch.Range.SequenceCeiling));

            _commandBlock.Post(Command.Completed(batch.Range));
        }
    }
}
