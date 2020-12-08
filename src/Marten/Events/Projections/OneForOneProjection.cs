using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections.Async;

namespace Marten.Events.Projections
{
    [Obsolete("This is going away in V4")]
    public class OneForOneProjection<TEvent, TView>: DocumentProjection<TView>, IDocumentProjection
    {
        private readonly ITransform<TEvent, TView> _transform;

        public OneForOneProjection(ITransform<TEvent, TView> transform)
        {
            _transform = transform;

            Consumes = new[] { typeof(TEvent) };
        }

        public void Apply(IDocumentSession session, EventPage page)
        {
            foreach (var stream in page.Streams)
            {
                foreach (var @event in stream.Events.OfType<Event<TEvent>>())
                {
                    session.Store(_transform.Transform(stream, @event));
                }
            }
        }

        public Task ApplyAsync(IDocumentSession session, EventPage page, CancellationToken token)
        {
            Apply(session, page);

            return Task.CompletedTask;
        }

        public Type[] Consumes { get; }

        public AsyncOptions AsyncOptions { get; } = new AsyncOptions();
    }
}
