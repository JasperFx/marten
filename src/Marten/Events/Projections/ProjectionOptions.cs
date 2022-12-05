using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Exceptions;

namespace Marten.Events.Projections;

/// <summary>
///     Used to register projections with Marten
/// </summary>
public class ProjectionOptions: DaemonSettings
{
    private readonly Dictionary<Type, object> _liveAggregateSources = new();
    private readonly StoreOptions _options;

    private Lazy<Dictionary<string, AsyncProjectionShard>> _asyncShards;
    private ImHashMap<Type, object> _liveAggregators = ImHashMap<Type, object>.Empty;

    internal ProjectionOptions(StoreOptions options)
    {
        _options = options;
    }

    internal IList<IProjectionSource> All { get; } = new List<IProjectionSource>();

    internal IList<AsyncProjectionShard> BuildAllShards(DocumentStore store)
    {
        return All.SelectMany(x => x.AsyncProjectionShards(store)).ToList();
    }

    internal bool DoesPersistAggregate(Type aggregateType)
    {
        return All.OfType<IAggregateProjection>().Any(x =>
            x.AggregateType == aggregateType && x.Lifecycle != ProjectionLifecycle.Live);
    }

    internal bool HasAnyAsyncProjections()
    {
        return All.Any(x => x.Lifecycle == ProjectionLifecycle.Async);
    }

    internal IEnumerable<Type> AllAggregateTypes()
    {
        foreach (var kv in _liveAggregators.Enumerate()) yield return kv.Key;

        foreach (var projection in All.OfType<IAggregateProjection>()) yield return projection.AggregateType;
    }

    internal IProjection[] BuildInlineProjections(DocumentStore store)
    {
        var inlineSources = All.Where(x => x.Lifecycle == ProjectionLifecycle.Inline).ToArray();

        return inlineSources.Select(x =>
        {
            try
            {
                return x.Build(store);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error trying to build an IProjection for projection " + x.ProjectionName);
                Console.WriteLine(e.ToString());

                throw;
            }
        }).ToArray();
    }


    /// <summary>
    ///     Register a projection to the Marten configuration
    /// </summary>
    /// <param name="projection">Value values are Inline/Async, The default is Inline</param>
    /// <param name="lifecycle"></param>
    /// <param name="projectionName">
    ///     Overwrite the named identity of this projection. This is valuable if using the projection
    ///     asynchonously
    /// </param>
    /// <param name="asyncConfiguration">
    ///     Optional configuration including teardown instructions for the usage of this
    ///     projection within the async projection daempon
    /// </param>
    public void Add(IProjection projection, ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline,
        string projectionName = null, Action<AsyncOptions> asyncConfiguration = null)
    {
        if (lifecycle == ProjectionLifecycle.Live)
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycle),
                $"{nameof(ProjectionLifecycle.Live)} cannot be used for IProjection");
        }

        if (projection is ProjectionBase p)
        {
            p.AssembleAndAssertValidity();
            p.Lifecycle = lifecycle;
        }

        if (projection is IProjectionSource source)
        {
            asyncConfiguration?.Invoke(source.Options);
            All.Add(source);
        }
        else
        {
            var wrapper = new ProjectionWrapper(projection, lifecycle);
            if (projectionName.IsNotEmpty())
            {
                wrapper.ProjectionName = projectionName;
            }

            asyncConfiguration?.Invoke(wrapper.Options);
            All.Add(wrapper);
        }
    }

    /// <summary>
    ///     Add a projection that will be executed inline
    /// </summary>
    /// <param name="projection"></param>
    /// <param name="lifecycle">Optionally override the lifecycle of this projection. The default is Inline</param>
    public void Add(EventProjection projection, ProjectionLifecycle? lifecycle = null)
    {
        if (lifecycle.HasValue)
        {
            projection.Lifecycle = lifecycle.Value;
        }

        projection.AssembleAndAssertValidity();
        All.Add(projection);
    }

    /// <summary>
    ///     Use a "self-aggregating" aggregate of type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="lifecycle">Override the aggregate lifecycle. The default is Inline</param>
    /// <returns>The extended storage configuration for document T</returns>
    public MartenRegistry.DocumentMappingExpression<T> SelfAggregate<T>(ProjectionLifecycle? lifecycle = null)
    {
        // Make sure there's a DocumentMapping for the aggregate
        var expression = _options.Schema.For<T>();

        var source = new SingleStreamAggregation<T> { Lifecycle = lifecycle ?? ProjectionLifecycle.Inline };
        source.AssembleAndAssertValidity();
        All.Add(source);

        return expression;
    }


    /// <summary>
    ///     Register an aggregate projection that should be evaluated inline
    /// </summary>
    /// <typeparam name="TProjection">Projection type</typeparam>
    /// <param name="lifecycle">Optionally override the ProjectionLifecycle</param>
    /// <returns>The extended storage configuration for document T</returns>
    public void Add<TProjection>(ProjectionLifecycle? lifecycle = null) where TProjection : GeneratedProjection, new()
    {
        var projection = new TProjection();

        if (lifecycle.HasValue)
        {
            projection.Lifecycle = lifecycle.Value;
        }

        projection.AssembleAndAssertValidity();

        All.Add(projection);
    }

    /// <summary>
    ///     Register an aggregate projection that should be evaluated inline
    /// </summary>
    /// <param name="projection"></param>
    /// <typeparam name="T"></typeparam>
    /// <param name="lifecycle">Optionally override the ProjectionLifecycle</param>
    /// <returns>The extended storage configuration for document T</returns>
    public void Add<T>(GeneratedAggregateProjectionBase<T> projection, ProjectionLifecycle? lifecycle = null)
    {
        if (lifecycle.HasValue)
        {
            projection.Lifecycle = lifecycle.Value;
        }

        projection.AssembleAndAssertValidity();

        All.Add(projection);
    }

    internal bool Any()
    {
        return All.Any();
    }

    internal ILiveAggregator<T> AggregatorFor<T>() where T : class
    {
        if (_liveAggregators.TryFind(typeof(T), out var aggregator))
        {
            return (ILiveAggregator<T>)aggregator;
        }

        var source = tryFindProjectionSourceForAggregateType<T>();
        source.AssembleAndAssertValidity();

        aggregator = source.As<ILiveAggregatorSource<T>>().Build(_options);
        _liveAggregators = _liveAggregators.AddOrUpdate(typeof(T), aggregator);

        return (ILiveAggregator<T>)aggregator;
    }

    private SingleStreamAggregation<T> tryFindProjectionSourceForAggregateType<T>() where T : class
    {
        var candidate = All.OfType<SingleStreamAggregation<T>>().FirstOrDefault();
        if (candidate != null)
        {
            return candidate;
        }

        if (!_liveAggregateSources.TryGetValue(typeof(T), out var source))
        {
            return new SingleStreamAggregation<T>();
        }

        return source as SingleStreamAggregation<T>;
    }

    internal void AssertValidity(DocumentStore store)
    {
        var duplicateNames = All
            .GroupBy(x => x.ProjectionName)
            .Where(x => x.Count() > 1)
            .Select(group => $"Duplicate projection name '{group.Key}': {group.Select(x => x.ToString()).Join(", ")}")
            .ToArray();

        if (duplicateNames.Any())
        {
            throw new InvalidOperationException(duplicateNames.Join("; "));
        }


        var messages = All.Concat(_liveAggregateSources.Values)
            .OfType<GeneratedProjection>()
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

    internal bool TryFindProjection(string projectionName, out IProjectionSource source)
    {
        source = All.FirstOrDefault(x => x.ProjectionName.EqualsIgnoreCase(projectionName));
        return source != null;
    }


    internal string[] AllProjectionNames()
    {
        return All.Select(x => $"'{x.ProjectionName}'").ToArray();
    }

    internal IEnumerable<Type> AllPublishedTypes()
    {
        return All.OfType<IProjectionSource>().SelectMany(x => x.PublishedTypes()).Distinct();
    }
}
