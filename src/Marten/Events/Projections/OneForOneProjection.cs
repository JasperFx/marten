using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;

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
            streams
                .SelectMany(x => x.Events)
                .OfType<Event<TEvent>>()
                .Select(x => _transform.Transform(x))
                .Each(x => session.Store(x));
        }

        public Task ApplyAsync(IDocumentSession session, EventStream[] streams, CancellationToken token)
        {
            streams
                .SelectMany(x => x.Events)
                .OfType<Event<TEvent>>()
                .Select(x => _transform.Transform(x))
                .Each(x => session.Store(x));

            return Task.CompletedTask;
        }
    }
}