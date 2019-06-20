using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public interface IDaemon: IDisposable
    {
        void StartAll();

        Task StopAll();

        Task Stop<T>();

        Task Stop(Type viewType);

        void Start<T>(DaemonLifecycle lifecycle);

        void Start(Type viewType, DaemonLifecycle lifecycle);

        Task WaitUntilEventIsProcessed(long sequence, CancellationToken token = new CancellationToken());

        Task WaitForNonStaleResults(CancellationToken token = new CancellationToken());

        Task WaitForNonStaleResultsOf<T>(CancellationToken token = new CancellationToken());

        Task WaitForNonStaleResultsOf(Type viewType, CancellationToken token = new CancellationToken());

        IEnumerable<IProjectionTrack> AllActivity { get; }

        IProjectionTrack TrackFor<T>();

        IProjectionTrack TrackFor(Type viewType);

        Task RebuildAll(CancellationToken token = new CancellationToken());

        Task Rebuild<T>(CancellationToken token = default(CancellationToken));

        Task Rebuild(Type viewType, CancellationToken token = default(CancellationToken));

        IDaemonLogger Logger { get; }
    }
}
