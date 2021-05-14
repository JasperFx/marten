using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Baseline.ImTools;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Exceptions;

namespace Marten.Events.Projections
{
    /// <summary>
    /// Used to register projections with Marten
    /// </summary>
    public class ProjectionOptions
    {
        private readonly StoreOptions _options;
        private readonly Dictionary<Type, object> _liveAggregateSources = new Dictionary<Type, object>();
        private ImHashMap<Type, object> _liveAggregators = ImHashMap<Type, object>.Empty;

        private Lazy<Dictionary<string, AsyncProjectionShard>> _asyncShards;

        internal ProjectionOptions(StoreOptions options)
        {
            _options = options;
        }

        internal IList<ProjectionSource> All { get; } = new List<ProjectionSource>();

        internal IList<AsyncProjectionShard> BuildAllShards(DocumentStore store)
        {
            return All.SelectMany(x => x.AsyncProjectionShards(store)).ToList();
        }

        internal bool HasAnyAsyncProjections()
        {
            return All.Any(x => x.Lifecycle == ProjectionLifecycle.Async);
        }

        internal IEnumerable<Type> AllAggregateTypes()
        {
            foreach (var kv in _liveAggregators.Enumerate())
            {
                yield return kv.Key;
            }

            foreach (var projection in All.OfType<IAggregateProjection>())
            {
                yield return projection.AggregateType;
            }
        }

        internal IProjection[] BuildInlineProjections(DocumentStore store)
        {
            return All.Where(x => x.Lifecycle == ProjectionLifecycle.Inline).Select(x => x.Build(store)).ToArray();
        }


        /// <summary>
        /// Register a projection to the Marten configuration
        /// </summary>
        /// <param name="projection">Value values are Inline/Async, The default is Inline</param>
        /// <param name="lifecycle"></param>
        /// <param name="projectionName">Overwrite the named identity of this projection. This is valuable if using the projection asynchonously</param>
        /// <param name="asyncConfiguration">Optional configuration including teardown instructions for the usage of this projection within the async projection daempon</param>
        public void Add(IProjection projection, ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline,
            string projectionName = null, Action<AsyncOptions> asyncConfiguration = null)
        {
            if (lifecycle == ProjectionLifecycle.Live)
            {
                throw new ArgumentOutOfRangeException(nameof(lifecycle),
                    $"{nameof(ProjectionLifecycle.Live)} cannot be used for IProjection");
            }


            var wrapper = new ProjectionWrapper(projection, lifecycle);
            if (projectionName.IsNotEmpty())
            {
                wrapper.ProjectionName = projectionName;
            }

            asyncConfiguration?.Invoke(wrapper.Options);
            All.Add(wrapper);
        }

        /// <summary>
        /// Add a projection that will be executed inline
        /// </summary>
        /// <param name="projection"></param>
        /// <param name="lifecycle">Optionally override the lifecycle of this projection. The default is Inline</param>
        public void Add(EventProjection projection, ProjectionLifecycle? lifecycle = null)
        {
            if (lifecycle.HasValue)
            {
                projection.Lifecycle = lifecycle.Value;
            }
            projection.AssertValidity();
            All.Add(projection);
        }

        /// <summary>
        /// Use a "self-aggregating" aggregate of type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="lifecycle">Override the aggregate lifecycle. The default is Inline</param>
        /// <returns>The extended storage configuration for document T</returns>
        public MartenRegistry.DocumentMappingExpression<T> SelfAggregate<T>(ProjectionLifecycle? lifecycle = null)
        {
            // Make sure there's a DocumentMapping for the aggregate
            var expression = _options.Schema.For<T>();

            var source = new AggregateProjection<T>()
            {
                Lifecycle = lifecycle ?? ProjectionLifecycle.Inline
            };
            source.AssertValidity();
            All.Add(source);

            return expression;
        }



        /// <summary>
        /// Register an aggregate projection that should be evaluated inline
        /// </summary>
        /// <typeparam name="TProjection">Projection type</typeparam>
        /// <param name="lifecycle">Optionally override the ProjectionLifecycle</param>
        /// <returns>The extended storage configuration for document T</returns>
        public void Add<TProjection>(ProjectionLifecycle? lifecycle = null) where TProjection: ProjectionSource, new()
        {
            var projection = new TProjection();
            if (lifecycle.HasValue)
            {
                projection.Lifecycle = lifecycle.Value;
            }

            projection.AssertValidity();

            All.Add(projection);
        }

        /// <summary>
        /// Register an aggregate projection that should be evaluated inline
        /// </summary>
        /// <param name="projection"></param>
        /// <typeparam name="T"></typeparam>
        /// <param name="lifecycle">Optionally override the ProjectionLifecycle</param>
        /// <returns>The extended storage configuration for document T</returns>
        public MartenRegistry.DocumentMappingExpression<T> Add<T>(AggregateProjection<T> projection, ProjectionLifecycle? lifecycle = null)
        {
            var expression = _options.Schema.For<T>();
            if (lifecycle.HasValue)
            {
                projection.Lifecycle = lifecycle.Value;
            }

            projection.AssertValidity();

            All.Add(projection);

            return expression;
        }

        internal bool Any()
        {
            return All.Any();
        }

        internal ILiveAggregator<T> AggregatorFor<T>() where T : class
        {
            if (_liveAggregators.TryFind(typeof(T), out var aggregator))
            {
                return (ILiveAggregator<T>) aggregator;
            }

            var source = tryFindProjectionSourceForAggregateType<T>();
            source.AssertValidity();

            aggregator = source.As<ILiveAggregatorSource<T>>().Build(_options);
            _liveAggregators = _liveAggregators.AddOrUpdate(typeof(T), aggregator);

            return (ILiveAggregator<T>) aggregator;
        }

        private AggregateProjection<T> tryFindProjectionSourceForAggregateType<T>() where T : class
        {
            var candidate = All.OfType<AggregateProjection<T>>().FirstOrDefault();
            if (candidate != null)
            {
                return candidate;
            }

            if (!_liveAggregateSources.TryGetValue(typeof(T), out var source))
            {
                return new AggregateProjection<T>();
            }

            return source as AggregateProjection<T>;
        }

        internal void AssertValidity(DocumentStore store)
        {
            var messages = All.Concat(_liveAggregateSources.Values)
                .OfType<ProjectionSource>()
                .Distinct()
                .SelectMany(x => x.ValidateConfiguration(_options))
                .ToArray();

            _asyncShards = new Lazy<Dictionary<string, AsyncProjectionShard>>(() =>
            {
                return All
                    .Where(x => x.Lifecycle == ProjectionLifecycle.Async)
                    .SelectMany(x => x.AsyncProjectionShards(store))
                    .ToDictionary(x => x.Name.Identity);

            });

            if (messages.Any())
            {
                throw new InvalidProjectionException(messages);
            }
        }

        internal IReadOnlyList<AsyncProjectionShard> AllShards()
        {
            return _asyncShards.Value.Values.ToList();
        }

        internal bool TryFindAsyncShard(string projectionOrShardName, out AsyncProjectionShard shard)
        {
            return _asyncShards.Value.TryGetValue(projectionOrShardName, out shard);
        }

        internal bool TryFindProjection(string projectionName, out ProjectionSource source)
        {
            source = All.FirstOrDefault(x => x.ProjectionName.EqualsIgnoreCase(projectionName));
            return source != null;
        }


        internal string[] AllProjectionNames()
        {
            return All.Select(x => $"'{x.ProjectionName}'").ToArray();
        }
    }
}
