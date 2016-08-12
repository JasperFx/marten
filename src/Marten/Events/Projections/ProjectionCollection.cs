using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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
            if (projection == null) throw new ArgumentNullException(nameof(projection));
            if (projection.Produces == null)
            {
                throw new InvalidOperationException("projection.Produces is null. Projection should defined the produced projection type.");
            }

            _options.MappingFor(projection.Produces);
            _projections.Add(projection);
        }

        public IProjection ForView(Type viewType)
        {
            return _projections.FirstOrDefault(x => x.Produces == viewType);
        }
    }
}