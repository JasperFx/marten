using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Internal.Sessions;

namespace Marten.Events.Daemon
{

    // TODO -- need a Drain() method
    // TODO -- need a Dispose that really cleans things off
    internal class ProjectionAgent : IProjectionUpdater
    {
        private readonly DocumentStore _store;
        private readonly IAsyncProjectionShard _projectionShard;
        private ITargetBlock<EventRange> _hopper;
        private readonly ProjectionController _controller;
        private readonly ActionBlock<Command> _commandBlock;
        private readonly TransformBlock<EventRange, EventRange> _loader;
        private EventFetcher _fetcher;

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

        public async Task Start(long highWaterMark)
        {
            _fetcher = new EventFetcher(_store, _projectionShard.EventFilters);
            _hopper = _projectionShard.Start(this);
            _loader.LinkTo(_hopper);

            var lastCommitted = await _store.Events.ProjectionProgressFor(_projectionShard.ProjectionOrShardName);

            _commandBlock.Post(Command.Started(highWaterMark, lastCommitted));
        }

        public string ProjectionOrShardName => _projectionShard.ProjectionOrShardName;

        public void MarkHighWater(long sequence) => _commandBlock.Post(
            Command.HighWaterMarkUpdated(sequence));

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
                // TODO -- use a real cancellation
                await session.ExecuteBatchAsync(batch, CancellationToken.None);
            }

            batch.Dispose();



            // TODO -- error handling
            // TODO -- instrumentation
            // TODO -- re-evaluate what should be happening next

            _commandBlock.Post(Command.Completed(batch.Range));
        }
    }
}
