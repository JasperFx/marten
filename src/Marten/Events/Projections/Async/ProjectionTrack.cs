using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
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
            // TODO -- track the From and To, make sure we don't have any gaps. From would need to be equal to Last

            await _projection.ApplyAsync(_session, page.Streams, cancellation).ConfigureAwait(false);

            _session.QueueOperation(new EventProgressWrite(_events, _projection.Produces.FullName, page.To));

            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);
        }
    }
}