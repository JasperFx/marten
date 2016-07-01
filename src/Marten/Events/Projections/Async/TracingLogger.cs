using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Events.Projections.Async
{
    public class TracingLogger : IDaemonLogger
    {
        private readonly Action<string> _writeline;

        public TracingLogger(Action<string> writeline)
        {
            _writeline = writeline;
        }

        public void BeginStartAll(IEnumerable<IProjectionTrack> values)
        {
            _writeline($"Starting all tracks: {values.Select(x => x.ViewType.FullName).Join(", ")}");
        }

        public void DeterminedStartingPosition(IProjectionTrack track)
        {
            _writeline($"Projection {track.ViewType.FullName} is starting > {track.LastEncountered}");
        }

        public void FinishedStartingAll()
        {
            _writeline("Finished starting the async daemon");
        }

        public void BeginRebuildAll(IEnumerable<IProjectionTrack> values)
        {
            _writeline($"Beginning a Rebuild of {values.Select(x => x.ViewType.FullName).Join(", ")}");
        }

        public void FinishRebuildAll(TaskStatus status, AggregateException exception)
        {
            _writeline($"Finished RebuildAll with status {status}");
            if (exception != null)
            {
                var flattened = exception.Flatten();

                foreach (var ex in flattened.InnerExceptions)
                {
                    _writeline("");
                    _writeline("---------");
                    _writeline("Exception:");
                    _writeline("---------");
                    _writeline(ex.ToString());
                    _writeline("");
                }
            }
        }

        public void BeginStopAll()
        {
            _writeline("Beginning to stop the Async Daemon");
        }

        public void AllStopped()
        {
            _writeline("Daemon stopped successfully");
        }

        public void PageFetched(IProjectionTrack track, EventPage page)
        {
            //_writeline($"{page} fetched for {track.ViewType.FullName}");
        }

        public void PausingFetching(IProjectionTrack track, long lastEncountered)
        {
            _writeline($"Pausing fetching for {track.ViewType.FullName}, last encountered {lastEncountered}");
        }

        public void FetchStarted(IProjectionTrack track)
        {
            _writeline($"Starting fetching for {track.ViewType.FullName}");
        }

        public void FetchingIsAtEndOfEvents(IProjectionTrack track)
        {
            _writeline($"Fetching is at the end of the event log for {track.ViewType.FullName}");
        }

        public void FetchingStopped(IProjectionTrack track)
        {
            _writeline($"Stopped event fetching for {track.ViewType.FullName}");
        }

        public void ExecutingPage(EventPage page, IProjectionTrack track)
        {
            //_writeline($"Executing {page} for projection {track.ViewType.FullName}");
        }

        public void PageExecuted(EventPage page, IProjectionTrack track)
        {
            _writeline($"{page} executed for {track.ViewType.FullName}");
        }

        public void FetchingFinished(IProjectionTrack track, long lastEncountered)
        {
            _writeline($"Fetching finished for {track.ViewType.FullName} at event {lastEncountered}");
        }

        public void StartingProjection(IProjectionTrack track, DaemonLifecycle lifecycle)
        {
            _writeline($"Starting projection {track.ViewType.FullName} running as {lifecycle}");
        }

        public void Stopping(IProjectionTrack track)
        {
            _writeline($"Stopping projection {track.ViewType.FullName}");
        }

        public void Stopped(IProjectionTrack track)
        {
            _writeline($"Stopped projection {track.ViewType.FullName}");
        }

        public void ProjectionBackedUp(IProjectionTrack track, int cachedEventCount, EventPage page)
        {
            _writeline($"Projection {track.ViewType.FullName} is backed up with {cachedEventCount} events in memory, last page fetched was {page}");
        }

        public void ClearingExistingState(IProjectionTrack track)
        {
            _writeline($"Clearing the existing state for projection {track.ViewType.FullName}");
        }

        public void Error(Exception exception)
        {
            _writeline(exception.ToString());
        }
    }
}