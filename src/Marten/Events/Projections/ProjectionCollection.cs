using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using ImTools;
using Marten.Events.Aggregation;

namespace Marten.Events.Projections
{
    // TODO -- well, everything. Add Xml comments
    public class ProjectionCollection
    {
        private readonly StoreOptions _options;
        private readonly Dictionary<Type, object> _liveAggregateSources = new Dictionary<Type, object>();
        private ImHashMap<Type, object> _liveAggregators = ImHashMap<Type, object>.Empty;

        private readonly IList<IProjectionSource> _inlineProjections = new List<IProjectionSource>();
        private readonly IList<IProjectionSource> _asyncProjections = new List<IProjectionSource>();

        internal ProjectionCollection(StoreOptions options)
        {
            _options = options;
        }

        internal IEnumerable<Type> AllAggregateTypes()
        {
            foreach (var kv in _liveAggregators.Enumerate())
            {
                yield return kv.Key;
            }

            foreach (var projection in _inlineProjections.Concat(_asyncProjections).OfType<IAggregateProjection>())
            {
                yield return projection.AggregateType;
            }
        }

        internal IInlineProjection[] BuildInlineProjections()
        {
            return _inlineProjections.Select(x => x.BuildInline(_options)).ToArray();
        }

        public void Inline(IInlineProjection projection)
        {
            _inlineProjections.Add(new InlineProjectionSource(projection));
        }

        public void Async(IInlineProjection projection)
        {
            _asyncProjections.Add(new InlineProjectionSource(projection));
        }

        public void Inline(EventProjection projection)
        {
            projection.As<IValidatedProjection>().AssertValidity();
            _inlineProjections.Add(projection);
        }

        public void Async(EventProjection projection)
        {
            projection.As<IValidatedProjection>().AssertValidity();
            _asyncProjections.Add(projection);
        }

        public void InlineSelfAggregate<T>()
        {
            _options.Storage.MappingFor(typeof(T));
            var source = new AggregateProjection<T>();
            source.As<IValidatedProjection>().AssertValidity();
            _inlineProjections.Add(source);
        }

        public void AsyncSelfAggregate<T>()
        {
            _options.Storage.MappingFor(typeof(T));
            var source = new AggregateProjection<T>();
            source.As<IValidatedProjection>().AssertValidity();
            _asyncProjections.Add(source);
        }

        public void Inline<T>(AggregateProjection<T> projection)
        {
            projection.As<IValidatedProjection>().AssertValidity();
            _options.Storage.MappingFor(typeof(T));
            _inlineProjections.Add(projection);
        }

        public void Async<T>(AggregateProjection<T> projection)
        {
            projection.As<IValidatedProjection>().AssertValidity();
            _options.Storage.MappingFor(typeof(T));
            _asyncProjections.Add(projection);
        }

        internal bool Any()
        {
            return _asyncProjections.Any() || _inlineProjections.Any();
        }

        public ILiveAggregator<T> AggregatorFor<T>() where T : class
        {
            if (_liveAggregators.TryFind(typeof(T), out var aggregator))
            {
                return (ILiveAggregator<T>) aggregator;
            }

            if (!_liveAggregateSources.TryGetValue(typeof(T), out var source))
            {
                // TODO -- there needs to be a validating step here I think to see if there are any projection methods.

                source = new AggregateProjection<T>();
                source.As<IValidatedProjection>().AssertValidity();
            }

            aggregator = source.As<ILiveAggregatorSource<T>>().Build(_options);
            _liveAggregators = _liveAggregators.AddOrUpdate(typeof(T), aggregator);

            return (ILiveAggregator<T>) aggregator;
        }
    }
}
