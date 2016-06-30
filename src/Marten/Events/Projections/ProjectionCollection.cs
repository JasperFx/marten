using System.Collections;
using System.Collections.Generic;
using Marten.Schema;

namespace Marten.Events.Projections
{
    public class ProjectionCollection : IEnumerable<IProjection>
    {
        private readonly StoreOptions _options;
        private readonly IList<IProjection> _projections = new List<IProjection>();

        public ProjectionCollection(StoreOptions options)
        {
            _options = options;
        }

        public IEnumerator<IProjection> GetEnumerator()
        {
            return _projections.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public AggregationProjection<T> AggregateStreamsWith<T>() where T : class, new()
        {
            _options.MappingFor(typeof(T));

            var aggregator = new Aggregator<T>();
            var finder = new AggregateFinder<T>();
            var projection = new AggregationProjection<T>(finder, aggregator);

            Add(projection);

            return projection;
        }

        public OneForOneProjection<TEvent, TView> TransformEvents<TEvent, TView>(ITransform<TEvent, TView> transform)
        {
            var projection = new OneForOneProjection<TEvent, TView>(transform);
            Add(projection);

            return projection;
        }

        public void Add(IProjection projection)
        {
            _options.MappingFor(projection.Produces);
            _projections.Add(projection);
        }
    }
}