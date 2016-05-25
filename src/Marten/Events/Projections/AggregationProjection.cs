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
        private readonly Aggregator<T> _aggregator;

        public AggregationProjection(IAggregationFinder<T> finder, Aggregator<T> aggregator)
        {
            _finder = finder;
            _aggregator = aggregator;
        }

        public void Apply(IDocumentSession session)
        {
            MatchingStreams(session).Each(stream =>
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

        public async Task ApplyAsync(IDocumentSession session, CancellationToken token)
        {
            foreach (var stream in MatchingStreams(session))
            {
                var state = await _finder.FindAsync(stream, session, token).ConfigureAwait(false) ?? new T();
                update(state, stream);

                session.Store(state);
            }
        }



        public EventStream[] MatchingStreams(IDocumentSession session)
        {
            return session.PendingChanges.Streams()
                .Where(_aggregator.AppliesTo).ToArray();
        }

    }
}