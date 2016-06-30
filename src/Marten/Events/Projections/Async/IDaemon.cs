using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public interface IDaemon : IDisposable
    {
        Task RebuildProjection<T>(CancellationToken token);

        void StartAll();

        Task StopAll();

        Task Stop<T>();
        Task Stop(Type viewType);

        void Start<T>();
        void Start(Type viewType);

        Task<long> WaitUntilEventIsProcessed(long sequence);

        Task WaitForNonStaleResults();

        Task WaitForNonStaleResultsOf<T>();
        Task WaitForNonStaleResultsOf(Type viewType);

        IEnumerable<IProjectionTrack> AllActivity { get; }

        IProjectionTrack TrackFor<T>();

        IProjectionTrack TrackFor(Type viewType);

        Task RebuildAll();
    }
}