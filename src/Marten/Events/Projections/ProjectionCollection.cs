using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using System.Reflection;

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
            var aggregator = _options.Events.AggregateFor<T>();

            IAggregationFinder<T> finder = _options.Events.StreamIdentity == StreamIdentity.AsGuid
                ? (IAggregationFinder<T>)new AggregateFinder<T>()
                : new StringIdentifiedAggregateFinder<T>();

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

            if (projection is IDocumentProjection)
            {
                _options.Storage.MappingFor(projection.ProjectedType());
            }

            _projections.Add(projection);
        }

        public void Add<T>() where T : IProjection, new()
        {
            Add(new T());
        }

        public void Add<T>(Func<T> projectionFactory) where T : IProjection, new()
        {
            var lazyLoadedProjection = new LazyLoadedProjection<T>(projectionFactory);

            if (lazyLoadedProjection == null) throw new ArgumentNullException(nameof(lazyLoadedProjection));

            if (typeof(T).GetTypeInfo().IsAssignableFrom(typeof(IDocumentProjection).GetTypeInfo()))
            {
                _options.Storage.MappingFor(lazyLoadedProjection.ProjectedType());
            }

            _projections.Add(lazyLoadedProjection);
        }

        public IProjection ForView(Type viewType)
        {
            return _projections.FirstOrDefault(x => x.ProjectedType() == viewType);
        }
    }
}