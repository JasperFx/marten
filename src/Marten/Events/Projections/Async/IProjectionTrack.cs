using System;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public interface IProjectionTrack : IDisposable
    {
        long LastEncountered { get; }

        Type ViewType { get; }

        int QueuedPageCount { get; }

        void QueuePage(EventPage page);
        Task<long> WaitUntilEventIsProcessed(long sequence);
    }
}