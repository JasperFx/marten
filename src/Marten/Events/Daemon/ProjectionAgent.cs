using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Marten.Internal.Sessions;

namespace Marten.Events.Daemon
{
    internal class AgentCommand
    {
        public EventRange Completed { get; set; }

    }



    internal class ProjectionAgent : IProjectionUpdater
    {
        private readonly DocumentStore _store;
        private readonly IAsyncProjectionShard _projectionShard;
        private ITargetBlock<EventRange> _hopper;

        public ProjectionAgent(DocumentStore store, IAsyncProjectionShard projectionShard)
        {
            _store = store;
            _projectionShard = projectionShard;

            var singleFile = new ExecutionDataflowBlockOptions
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            };
        }

        public AgentStatus Status { get; private set; }


        public long LastCommittedSequence { get; private set; }
        public long LastLoadedCeiling { get; private set; }

        public long HighWaterSequence { get; private set; }

        public void StartRange(EventRange range)
        {
            throw new NotImplementedException();
        }

        public async Task Start()
        {
            // TODO -- need to look for the current projection status
            _hopper = _projectionShard.Start(this);



            // TODO - set a command message
        }

        private async Task commitUpdates(ProjectionUpdateBatch batch)
        {
            await batch.Queue.Completion;

            batch.Dispose();



            // TODO -- error handling
            // TODO -- instrumentation
            // TODO -- re-evaluate what should be happening next

            throw new System.NotImplementedException();
        }

        public string ProjectionOrShardName => _projectionShard.ProjectionOrShardName;

        public void MarkHighWater(long sequence)
        {
            // TODO -- decide whether to keep

        }

        public ProjectionUpdateBatch StartNewBatch(EventRange range)
        {
            var session = _store.LightweightSession();
            return new ProjectionUpdateBatch(_store.Events, (DocumentSessionBase) session, range);
        }

        public Task ExecuteBatch(ProjectionUpdateBatch batch)
        {
            throw new System.NotImplementedException();
        }

    }

    public interface IProjectionUpdater
    {
        ProjectionUpdateBatch StartNewBatch(EventRange range);
        Task ExecuteBatch(ProjectionUpdateBatch batch);

        void StartRange(EventRange range);
    }

    public class AsyncOptions
    {
        public int BatchSize { get; set; } = 500;
        public int MaximumHopperSize { get; set; } = 2500;
    }
}
