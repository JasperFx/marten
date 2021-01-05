using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using ImTools;
using Marten.Events.Aggregation;
using Marten.Exceptions;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Used to register projections with Marten
    /// </summary>
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


        /// <summary>
        /// Add a projection to be executed inline
        /// </summary>
        /// <param name="projection"></param>
        public void Inline(IInlineProjection projection)
        {
            _inlineProjections.Add(new InlineProjectionSource(projection));
        }

        /// <summary>
        /// Add a projection that should be executed asynchronously
        /// </summary>
        /// <param name="projection"></param>
        public void Async(IInlineProjection projection)
        {
            _asyncProjections.Add(new InlineProjectionSource(projection));
        }

        /// <summary>
        /// Add a projection that will be executed inline
        /// </summary>
        /// <param name="projection"></param>
        public void Inline(EventProjection projection)
        {
            projection.As<IValidatedProjection>().AssertValidity();
            _inlineProjections.Add(projection);
        }

        /// <summary>
        /// Add a projection that should be executed asynchronously
        /// </summary>
        /// <param name="projection"></param>
        public void Async(EventProjection projection)
        {
            projection.As<IValidatedProjection>().AssertValidity();
            _asyncProjections.Add(projection);
        }

        /// <summary>
        /// Use a "self-aggregating" aggregate of type as an inline projection
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>The extended storage configuration for document T</returns>
        public MartenRegistry.DocumentMappingExpression<T> InlineSelfAggregate<T>()
        {
            // Make sure there's a DocumentMapping for the aggregate
            var expression = _options.Schema.For<T>();
            var source = new AggregateProjection<T>();
            source.As<IValidatedProjection>().AssertValidity();
            _inlineProjections.Add(source);

            return expression;
        }

        /// <summary>
        /// Use a "self-aggregating" aggregate of type as an async projection
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns>The extended storage configuration for document T</returns>
        public MartenRegistry.DocumentMappingExpression<T> AsyncSelfAggregate<T>()
        {
            // Make sure there's a DocumentMapping for the aggregate
            var expression = _options.Schema.For<T>();
            var source = new AggregateProjection<T>();
            source.As<IValidatedProjection>().AssertValidity();
            _asyncProjections.Add(source);

            return expression;
        }

        /// <summary>
        /// Register an aggregate projection that should be evaluated inline
        /// </summary>
        /// <param name="projection"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns>The extended storage configuration for document T</returns>
        public MartenRegistry.DocumentMappingExpression<T> Inline<T>(AggregateProjection<T> projection)
        {
            var expression = _options.Schema.For<T>();
            projection.As<IValidatedProjection>().AssertValidity();
            _inlineProjections.Add(projection);

            return expression;
        }

        /// <summary>
        /// Register an aggregate projection that should be evaluated asynchronously
        /// </summary>
        /// <param name="projection"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns>The extended storage configuration for document T</returns>
        public MartenRegistry.DocumentMappingExpression<T> Async<T>(AggregateProjection<T> projection)
        {
            var expression = _options.Schema.For<T>();
            projection.As<IValidatedProjection>().AssertValidity();
            _asyncProjections.Add(projection);

            return expression;
        }

        internal bool Any()
        {
            return _asyncProjections.Any() || _inlineProjections.Any();
        }

        internal ILiveAggregator<T> AggregatorFor<T>() where T : class
        {
            if (_liveAggregators.TryFind(typeof(T), out var aggregator))
            {
                return (ILiveAggregator<T>) aggregator;
            }

            if (!_liveAggregateSources.TryGetValue(typeof(T), out var source))
            {
                source = new AggregateProjection<T>();
                source.As<IValidatedProjection>().AssertValidity();
            }

            aggregator = source.As<ILiveAggregatorSource<T>>().Build(_options);
            _liveAggregators = _liveAggregators.AddOrUpdate(typeof(T), aggregator);

            return (ILiveAggregator<T>) aggregator;
        }

        internal void AssertValidity()
        {
            var messages = _asyncProjections.Concat(_inlineProjections).Concat(_liveAggregateSources.Values)
                .OfType<IValidatedProjection>().Distinct().SelectMany(x => x.ValidateConfiguration(_options))
                .ToArray();

            if (messages.Any())
            {
                throw new InvalidProjectionException(messages);
            }
        }
    }
}
