using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Marten.Events.Projections.Async
{
    // Some tracks will be passive, others actively fetching until they're done

    // Tested through integration tests
    public class ProjectionTrack : IProjectionTrack
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly EventGraph _events;
        private readonly IProjection _projection;
        private readonly IDocumentSession _session;
        private readonly ActionBlock<EventPage> _track;

        private readonly IList<EventWaiter> _waiters = new List<EventWaiter>();

        public ProjectionTrack(EventGraph events, IProjection projection, IDocumentSession session)
        {
            _events = events;
            _projection = projection;
            _session = session;

            // TODO -- use this differently
            _cancellation = new CancellationTokenSource();

            _track = new ActionBlock<EventPage>(page => ExecutePage(page, _cancellation.Token));
        }

        public long LastEncountered { get; set; }

        public Type ViewType => _projection.Produces;

        public void QueuePage(EventPage page)
        {
            _track.Post(page);
        }

        public int QueuedPageCount => _track.InputCount;

        public void Dispose()
        {
            _waiters.Clear();
            _track.Complete();
        }

        public async Task ExecutePage(EventPage page, CancellationToken cancellation)
        {
            await _projection.ApplyAsync(_session, page.Streams, cancellation).ConfigureAwait(false);

            _session.QueueOperation(new EventProgressWrite(_events, _projection.Produces.FullName, page.To));

            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);

            Console.WriteLine($"Processed {page} for view {ViewType.FullName}");

            LastEncountered = page.To;

            evaluateWaiters();
        }

        private void evaluateWaiters()
        {
            var expiredWaiters = _waiters.Where(x => x.Sequence <= LastEncountered).ToArray();
            foreach (var waiter in expiredWaiters)
            {
                waiter.Completion.SetResult(LastEncountered);
                _waiters.Remove(waiter);
            }
        }

        public Task<long> WaitUntilEventIsProcessed(long sequence)
        {
            if (LastEncountered >= sequence) return Task.FromResult(sequence);

            var waiter = new EventWaiter(sequence);
            _waiters.Add(waiter);

            return waiter.Completion.Task;
        }
    }
}