using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events.Projections.Async;

namespace Marten.Events.Projections
{
    // This is mostly tested through integration tests and in the Storyteller suite
    public class AggregationProjection<T> : DocumentProjection<T>, IDocumentProjection where T : class, new()
    {
        private readonly IAggregationFinder<T> _finder;
        private readonly IAggregator<T> _aggregator;

        public AggregationProjection(IAggregationFinder<T> finder, IAggregator<T> aggregator)
        {
            _finder = finder;
            _aggregator = aggregator;
        }

        public void Apply(IDocumentSession session, EventPage page)
        {
            MatchingStreams(page).Each(stream =>
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

        public async Task ApplyAsync(IDocumentSession session, EventPage page, CancellationToken token)
        {
            var matchingStreams = MatchingStreams(page);

            await _finder.FetchAllAggregates(session, matchingStreams, token).ConfigureAwait(false);

            foreach (var stream in matchingStreams)
            {
                var state = await _finder.FindAsync(stream, session, token).ConfigureAwait(false) ?? new T();
                update(state, stream);

                session.Store(state);
            }
        }



        public Type[] Consumes => _aggregator.EventTypes;


        public EventStream[] MatchingStreams(EventPage streams)
        {
            return streams.Streams.Where(_aggregator.AppliesTo).ToArray();
        }

        public AsyncOptions AsyncOptions { get; } = new AsyncOptions();
    }
}