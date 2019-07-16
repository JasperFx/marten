using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events.Projections.Async;

namespace Marten.Events.Projections
{
    // This is mostly tested through integration tests and in the Storyteller suite
    public class AggregationProjection<T>: DocumentProjection<T>, IDocumentProjection where T : class, new()
    {
        private readonly IAggregationFinder<T> finder;
        private readonly IList<IProjectionEventHandler<T>> eventHandlers = new List<IProjectionEventHandler<T>>();

        public AggregationProjection(IAggregationFinder<T> finder, IAggregator<T> aggregator)
        {
            this.finder = finder;
            eventHandlers.Add(new ProjectionAggregateEventHandler<T>(aggregator));
        }

        public void Apply(IDocumentSession session, EventPage page)
        {
            MatchingStreams(page).Each(stream =>
            {
                var state = finder.Find(stream, session);

                update(session, state, stream);

                session.Store(state);
            });
        }

        private void update(IDocumentSession session, T state, EventStream stream)
        {
            stream.Events.SelectMany(@event => HandlersFor(@event.Data.GetType()).Select(handler => (Event: @event, Handler: handler)))
                .Each(x => x.Handler.Handle(session, state, x.Event));
        }

        private IEnumerable<IProjectionEventHandler<T>> HandlersFor(Type eventType)
        {
            return eventHandlers.Where(handler => handler.CanHandle(eventType));
        }

        public async Task ApplyAsync(IDocumentSession session, EventPage page, CancellationToken token)
        {
            var matchingStreams = MatchingStreams(page);

            await finder.FetchAllAggregates(session, matchingStreams, token).ConfigureAwait(false);

            foreach (var stream in matchingStreams)
            {
                var state = await finder.FindAsync(stream, session, token).ConfigureAwait(false) ?? new T();
                update(session, state, stream);
            }
        }

        public Type[] Consumes => eventHandlers.SelectMany(e => e.Handles).ToArray();

        public EventStream[] MatchingStreams(EventPage streams)
        {
            return streams.Streams.Where(stream => eventHandlers.Any(handler => handler.CanHandle(stream))).ToArray();
        }

        public AsyncOptions AsyncOptions { get; } = new AsyncOptions();

        public AggregationProjection<T> DeleteEvent<TEvent>(Func<TEvent, Guid> streamIdSelector)
        {
            return this;
        }
    }
}
