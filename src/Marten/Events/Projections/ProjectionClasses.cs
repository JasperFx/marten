using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;

namespace Marten.Events.Projections
{
    public interface IProjection
    {
        void Apply(IDocumentSession session);
        Task ApplyAsync(IDocumentSession session, CancellationToken token);
    }

    public interface ITransform<TInput, TOutput>
    {
        TOutput Transform(TInput input);
    }

    public interface IAggregation<TAggregate, TEvent>
    {
        void Apply(TAggregate aggregate, TEvent @event);
    }

    public interface IAggregationFinder<T>
    {
        T Find(EventStream stream, IDocumentSession session);

        // TODO -- make this use the batch query later
        Task<T> FindAsync(EventStream stream, IDocumentSession session, CancellationToken token);
    }

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
                var state = _finder.Find(stream, session) ?? new T();
                Update(state, stream);

                session.Store(state);
            });
        }

        private void Update(T state, EventStream stream)
        {
            stream.Events.Each(x => x.Apply(state, _aggregator));
        }

        public async Task ApplyAsync(IDocumentSession session, CancellationToken token)
        {
            foreach (var stream in MatchingStreams(session))
            {
                var state = await _finder.FindAsync(stream, session, token) ?? new T();
                Update(state, stream);

                session.Store(state);
            }
        }



        public EventStream[] MatchingStreams(IDocumentSession session)
        {
            return session.PendingChanges.AllChangedFor<EventStream>()
                .Where(_aggregator.AppliesTo).ToArray();
        }

    }

    public class OneForOneProjection<TInput, TOutput> : IProjection
    {
        private readonly ITransform<TInput, TOutput> _transform;

        public OneForOneProjection(ITransform<TInput, TOutput> transform)
        {
            _transform = transform;
        }

        public void Apply(IDocumentSession session)
        {
            session
                .PendingChanges.AllChangedFor<EventStream>()
                .SelectMany(x => x.Events)
                .Select(x => x.Data)
                .OfType<TInput>()
                .Select(x => _transform.Transform(x))
                .Each(x => session.Store(x));
        }

        public Task ApplyAsync(IDocumentSession session, CancellationToken token)
        {
            session
                .PendingChanges.AllChangedFor<EventStream>()
                .SelectMany(x => x.Events)
                .Select(x => x.Data)
                .OfType<TInput>()
                .Select(x => _transform.Transform(x))
                .Each(x => session.Store(x));

            return Task.CompletedTask;
        }
    }
}