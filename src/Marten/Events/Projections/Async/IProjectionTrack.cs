using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async.ErrorHandling;

namespace Marten.Events.Projections.Async
{
    public interface IProjectionTrack : IDisposable, IMonitoredActivity
    {
        long LastEncountered { get; set; }
        Type ViewType { get; }
        void Start(DaemonLifecycle lifecycle);
        Task<long> WaitUntilEventIsProcessed(long sequence);
        Task<long> RunUntilEndOfEvents(CancellationToken token = new CancellationToken());
        Task Rebuild(CancellationToken token = new CancellationToken());

        void QueuePage(EventPage page);
        void Finished(long lastEncountered);
    }
}