using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public interface IDaemonLogger
    {
        void BeginStartAll(IEnumerable<IProjectionTrack> values);
        void DeterminedStartingPosition(IProjectionTrack track);
        void FinishedStartingAll();
        void BeginRebuildAll(IEnumerable<IProjectionTrack> values);
        void FinishRebuildAll(TaskStatus status, AggregateException exception);
        void BeginStopAll();
        void AllStopped();
        void PageFetched(IProjectionTrack track, EventPage page);
        void PausingFetching(IProjectionTrack track, long lastEncountered);
        void FetchStarted(IProjectionTrack track);
        void FetchingIsAtEndOfEvents(IProjectionTrack track);
        void FetchingStopped(IProjectionTrack track);
        void ExecutingPage(EventPage page, IProjectionTrack track);
        void PageExecuted(EventPage page, IProjectionTrack track);
        void FetchingFinished(IProjectionTrack track, long lastEncountered);
        void StartingProjection(IProjectionTrack track, DaemonLifecycle lifecycle);
        void Stopping(IProjectionTrack track);
        void Stopped(IProjectionTrack track);
        void ProjectionBackedUp(IProjectionTrack track, int cachedEventCount, EventPage page);
        void ClearingExistingState(IProjectionTrack track);

        void Error(Exception exception);
    }
}