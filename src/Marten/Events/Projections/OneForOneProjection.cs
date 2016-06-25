using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Projections
{
    public class OneForOneProjection<TEvent, TView> : IProjection
    {
        private readonly ITransform<TEvent, TView> _transform;

        public OneForOneProjection(ITransform<TEvent, TView> transform)
        {
            _transform = transform;
        }

        public void Apply(IDocumentSession session, EventStream[] streams)
        {
            foreach (var stream in streams)
            {
                foreach (var @event in stream.Events.OfType<Event<TEvent>>())
                {
                    session.Store(_transform.Transform(stream, @event));
                }
            }
        }

        public Task ApplyAsync(IDocumentSession session, EventStream[] streams, CancellationToken token)
        {
            Apply(session, streams);

            return Task.CompletedTask;
        }
    }
}