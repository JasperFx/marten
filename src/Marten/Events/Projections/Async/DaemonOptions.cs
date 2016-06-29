using System;
using System.Collections.Generic;

namespace Marten.Events.Projections.Async
{
    public class DaemonOptions
    {
        private readonly EventGraph _events;

        public DaemonOptions(EventGraph events)
        {
            _events = events;
        }

        public string SchemaName => _events.DatabaseSchemaName;

        public string Name { get; set; } = Guid.NewGuid().ToString();
        public int PageSize { get; set; } = 100;
        public string[] EventTypeNames { get; set; } = new string[0];

        public int MaximumStagedEventCount { get; set; } = 1000;


        public void Aggregate<T>(IAggregator<T> aggregator, IAggregationFinder<T> finder = null) where T : class, new()
        {
            var projection = new AggregationProjection<T>(finder ?? new AggregateFinder<T>(), aggregator);
            Add(projection);
        }

        public void Aggregate<T>(Action<Aggregator<T>> configure) where T : class, new()
        {
            var aggregator = new Aggregator<T>();
            configure(aggregator);

            Aggregate(aggregator);
        }

        public void TransformEvents<TEvent, TView>(ITransform<TEvent, TView> transform)
        {
            var projection = new OneForOneProjection<TEvent, TView>(transform);
            Add(projection);
        }

        public void Add(IProjection projection)
        {
            Projections.Add(projection);
        }

        public IList<IProjection> Projections { get; } = new List<IProjection>();
    }
}