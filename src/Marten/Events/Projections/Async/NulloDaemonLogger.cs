using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public class NulloDaemonLogger: IDaemonLogger
    {
        public void BeginStartAll(IEnumerable<IProjectionTrack> values)
        {
        }

        public void DeterminedStartingPosition(IProjectionTrack track)
        {
        }

        public void FinishedStartingAll()
        {
        }

        public void BeginRebuildAll(IEnumerable<IProjectionTrack> values)
        {
        }

        public void FinishRebuildAll(TaskStatus status, AggregateException exception)
        {
        }

        public void BeginStopAll()
        {
        }

        public void AllStopped()
        {
        }

        public void PausingFetching(IProjectionTrack track, long lastEncountered)
        {
        }

        public void FetchStarted(IProjectionTrack track)
        {
        }

        public void FetchingIsAtEndOfEvents(IProjectionTrack track)
        {
        }

        public void FetchingStopped(IProjectionTrack track)
        {
        }

        public void PageExecuted(EventPage page, IProjectionTrack track)
        {
        }

        public void FetchingFinished(IProjectionTrack track, long lastEncountered)
        {
        }

        public void StartingProjection(IProjectionTrack track, DaemonLifecycle lifecycle)
        {
        }

        public void Stopping(IProjectionTrack track)
        {
        }

        public void Stopped(IProjectionTrack track)
        {
        }

        public void ProjectionBackedUp(IProjectionTrack track, int cachedEventCount, EventPage page)
        {
        }

        public void ClearingExistingState(IProjectionTrack track)
        {
        }

        public void Error(Exception exception)
        {
            Console.WriteLine(exception.ToString());
        }
    }
}
