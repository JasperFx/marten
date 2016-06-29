using System;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Marten.Events.Projections.Async
{
    public interface IProjectionTrack : IDisposable
    {
        long LastEncountered { get; }

        Type ViewType { get; }

        int QueuedPageCount { get; }
        ITargetBlock<IDaemonUpdate> Updater { get; set; }

        void QueuePage(EventPage page);
        Task<long> WaitUntilEventIsProcessed(long sequence);
        Task Stop();
    }
}