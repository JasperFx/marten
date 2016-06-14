using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Events.Projections
{
    // This is mostly tested through integration tests and in the Storyteller suite
    public class AggregationProjection<T> : IProjection where T : class, new()
    {
        private readonly IAggregationFinder<T> _finder;
        private readonly IAggregator<T> _aggregator;

        public AggregationProjection(IAggregationFinder<T> finder, IAggregator<T> aggregator)
        {
            _finder = finder;
            _aggregator = aggregator;
        }

        public void Apply(IDocumentSession session, EventStream[] streams)
        {
            MatchingStreams(streams).Each(stream =>
            {
                var state = _finder.Find(stream, session);

                update(state, stream);

                session.Store(state);
            });
        }

        private void update(T state, EventStream stream)
        {
            stream.Events.Each(x => x.Apply(state, _aggregator));
        }

        public async Task ApplyAsync(IDocumentSession session, EventStream[] streams, CancellationToken token)
        {
            foreach (var stream in MatchingStreams(streams))
            {
                var state = await _finder.FindAsync(stream, session, token).ConfigureAwait(false) ?? new T();
                update(state, stream);

                session.Store(state);
            }
        }



        public EventStream[] MatchingStreams(EventStream[] streams)
        {
            return streams.Where(_aggregator.AppliesTo).ToArray();
        }
    }
}