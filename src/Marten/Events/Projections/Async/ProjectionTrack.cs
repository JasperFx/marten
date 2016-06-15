using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections.Async
{
    public class ProjectionTrack
    {
        private readonly IProjection _projection;
        private readonly IDocumentSession _session;

        public ProjectionTrack(IProjection projection, IDocumentSession session)
        {
            _projection = projection;
            _session = session;
        }

        public async Task ExecutePage(EventPage page, CancellationToken cancellation)
        {
            // TODO -- track the From and To, make sure we don't have any gaps. From would need to be equal to Last

            await _projection.ApplyAsync(_session, page.Streams, cancellation).ConfigureAwait(false);

            // TODO -- do something to mark the progress of the staged event options

            await _session.SaveChangesAsync(cancellation).ConfigureAwait(false);
        }
    }
}