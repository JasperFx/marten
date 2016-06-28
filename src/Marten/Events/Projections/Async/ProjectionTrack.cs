using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    // Some tracks will be passive, others actively fetching until they're done
    public interface IProjectionTrack
    {
        long LastEncountered { get; }

        Type ViewType { get; }

        void QueuePage(EventPage page);

        bool Processing { get; }
    }

    public class ProjectionTrack
    {
        private readonly EventGraph _events;
        private readonly IProjection _projection;
        private readonly IDocumentSession _session;

        public ProjectionTrack(EventGraph events, IProjection projection, IDocumentSession session)
        {
            _events = events;
            _projection = projection;
            _session = session;
        }

        public async Task ExecutePage(EventPage page, CancellationToken cancellation)
        {
            await _projection.ApplyAsync(_session, page.Streams, cancellation).ConfigureAwait(false);

            _session.QueueOperation(new EventProgressWrite(_events, _projection.Produces.FullName, page.To));

            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);

            LastEncountered = page.To;
        }

        public long LastEncountered { get; set; }

        public Type ViewType => _projection.Produces;
    }
}