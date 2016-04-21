using System;
using System.Collections.Generic;
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

    public class Aggregator<T> : IProjection where T : class, new()
    {
        private readonly IAggregationFinder<T> _finder;
        private readonly IDictionary<Type, object> _aggregations = new Dictionary<Type, object>(); 

        public Aggregator(IAggregationFinder<T> finder)
        {
            _finder = finder;
        }

        public void Add<TEvent>(IAggregation<T, TEvent> aggregation)
        {
            if (_aggregations.ContainsKey(typeof (TEvent)))
            {
                _aggregations[typeof (TEvent)] = aggregation;
            }
            else
            {
                _aggregations.Add(typeof(TEvent), aggregation);
            }
        }

        public IAggregation<T, TEvent> AggregatorFor<TEvent>()
        {
            return _aggregations.ContainsKey(typeof (TEvent))
                ? _aggregations[typeof (TEvent)].As<IAggregation<T, TEvent>>()
                : null;
        }

        public bool AppliesTo(EventStream stream)
        {
            return stream.Events.Any(x => _aggregations.ContainsKey(x.Data.GetType()));
        }

        public EventStream[] MatchingStreams(IDocumentSession session)
        {
            return session.PendingChanges.AllChangedFor<EventStream>()
                .Where(AppliesTo).ToArray();
        }

        public void Update(T state, EventStream stream)
        {
            stream.Events.Each(x => x.Apply(state, this));
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

        public async Task ApplyAsync(IDocumentSession session, CancellationToken token)
        {
            foreach (var stream in MatchingStreams(session))
            {
                var state = await _finder.FindAsync(stream, session, token) ?? new T();
                Update(state, stream);

                session.Store(state);
            }
        }
    }

    public class Event<T> : Event
    {
        public Event(T data)
        {
            Data = data;
        }

        public override void Apply<TAggregate>(TAggregate state, Aggregator<TAggregate> aggregator) 
        {
            aggregator.AggregatorFor<T>()?.Apply(state, Data.As<T>());
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