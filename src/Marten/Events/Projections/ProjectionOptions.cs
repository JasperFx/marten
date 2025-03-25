using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core.Reflection;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using JasperFx.Events.Subscriptions;
using Marten.Events.Aggregation;
using Marten.Events.Fetching;
using Marten.Schema;
using Marten.Subscriptions;

namespace Marten.Events.Projections;

/// <summary>
///     Used to register projections with Marten
/// </summary>
public class ProjectionOptions: ProjectionGraph<IProjection, IDocumentOperations, IQuerySession>
{
    internal readonly IFetchPlanner[] _builtInPlanners =
        [new InlineFetchPlanner(), new AsyncFetchPlanner(), new LiveFetchPlanner()];

    private readonly StoreOptions _options;

    /// <summary>
    ///     Register session listeners that will ONLY be applied within the asynchronous daemon updates.
    /// </summary>
    public readonly List<IChangeListener> AsyncListeners = new();

    internal ProjectionOptions(StoreOptions options): base(options.EventGraph)
    {
        _options = options;
    }

    /// <summary>
    ///     Any custom or extended IFetchPlanner strategies for customizing FetchForWriting() behavior
    /// </summary>
    internal List<IFetchPlanner> FetchPlanners { get; } = new();

    /// <summary>
    ///     Opt into a performance optimization that directs Marten to always use the identity map for an
    ///     Inline single stream projection's aggregate type when FetchForWriting() is called. Default is false.
    ///     Do not use this if you manually alter the fetched aggregate from FetchForWriting() outside of Marten
    /// </summary>
    public bool UseIdentityMapForAggregates
    {
        get => _options.Events.UseIdentityMapForAggregates;
        set => _options.Events.UseIdentityMapForAggregates = value;
    }

    protected override void onAddProjection(object projection)
    {
        if (projection is IAggregateProjection { Lifecycle: ProjectionLifecycle.Live } aggregateProjection)
        {
            _options.Storage.MappingFor(aggregateProjection.AggregateType).SkipSchemaGeneration = true;
        }
    }

    internal IEnumerable<IFetchPlanner> allPlanners()
    {
        foreach (var planner in FetchPlanners) yield return planner;

        foreach (var planner in _builtInPlanners) yield return planner;
    }

    internal IInlineProjection<IDocumentOperations>[] BuildInlineProjections(DocumentStore store)
    {
        return All
            .Where(x => x.Lifecycle == ProjectionLifecycle.Inline)
            .Select(x => x.BuildForInline())
            .ToArray();
    }

    /// <summary>
    ///     Register live stream aggregation. It's needed for pre-building generated types
    ///     (Read more in https://martendb.io/configuration/prebuilding.html).
    ///     You don't need to call this method if you registered Snapshot for this entity type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="asyncConfiguration">Use it to define behaviour during projection rebuilds</param>
    /// <returns>The extended storage configuration for entity T</returns>
    public MartenRegistry.DocumentMappingExpression<T> LiveStreamAggregation<T>(
        Action<AsyncOptions> asyncConfiguration = null
    )
    {
        var expression = singleStreamProjection<T>(ProjectionLifecycle.Live, null, asyncConfiguration);

        // Hack to address https://github.com/JasperFx/marten/issues/2610
        _options.Storage.MappingFor(typeof(T)).SkipSchemaGeneration = true;

        return expression;
    }

    /// <summary>
    ///     Perform automated snapshot on each event for selected entity type
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="lifecycle">Override the snapshot lifecycle. The default is Inline</param>
    /// <param name="asyncConfiguration">
    ///     Optional configuration including teardown instructions for the usage of this
    ///     projection within the async projection daemon
    /// </param>
    /// <returns>The extended storage configuration for document T</returns>
    public MartenRegistry.DocumentMappingExpression<T> Snapshot<T>(
        SnapshotLifecycle lifecycle,
        Action<AsyncOptions> asyncConfiguration = null
    )
    {
        return singleStreamProjection<T>(lifecycle.Map(), null, asyncConfiguration);
    }

    /// <summary>
    ///     Perform automated snapshot on each event for selected entity type
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="lifecycle">Override the snapshot lifecycle. The default is Inline</param>
    /// <param name="configureProjection">Use it to further customize the projection.</param>
    /// <param name="asyncConfiguration">
    ///     Optional configuration including teardown instructions for the usage of this
    ///     projection within the async projection daemon
    /// </param>
    /// <returns>The extended storage configuration for document T</returns>
    public MartenRegistry.DocumentMappingExpression<T> Snapshot<T>(
        SnapshotLifecycle lifecycle,
        Action<ProjectionBase> configureProjection,
        Action<AsyncOptions> asyncConfiguration = null
    )
    {
        return singleStreamProjection<T>(lifecycle.Map(), configureProjection, asyncConfiguration);
    }

    private MartenRegistry.DocumentMappingExpression<T> singleStreamProjection<T>(
        ProjectionLifecycle lifecycle,
        Action<ProjectionBase> configureProjection = null,
        Action<AsyncOptions> asyncConfiguration = null
    )
    {
        if (typeof(T).CanBeCastTo<ProjectionBase>())
        {
            throw new InvalidOperationException(
                $"This registration mechanism can only be used for an aggregate type that is 'self-aggregating'. Please use the Projections.Add() API instead to register {typeof(T).FullNameInCode()}");
        }

        // Make sure there's a DocumentMapping for the aggregate
        var expression = _options.Schema.For<T>();

        var identityType = new DocumentMapping(typeof(T), _options).IdType;
        var source = typeof(SingleStreamProjection<,>).CloseAndBuildAs<ProjectionBase>(typeof(T), identityType);
        source.Lifecycle = lifecycle;

        configureProjection?.Invoke(source);

        asyncConfiguration?.Invoke(source.Options);

        source.AssembleAndAssertValidity();

        All.Add((IProjectionSource<IDocumentOperations, IQuerySession>)source);

        return expression;
    }


    /// <summary>
    ///     Add a new event subscription to this store with the option to configure the filtering
    ///     and async daemon behavior
    /// </summary>
    /// <param name="subscription"></param>
    /// <param name="configure"></param>
    public void Subscribe(ISubscription subscription, Action<ISubscriptionOptions>? configure = null)
    {
        var source = subscription as ISubscriptionSource<IDocumentOperations, IQuerySession> ??
                     new SubscriptionWrapper(subscription);

        if (source is ISubscriptionOptions options)
        {
            configure?.Invoke(options);
        }
        else if (configure != null)
        {
            throw new InvalidOperationException("Unable to apply subscription options to " + subscription);
        }

        registerSubscription(source);
    }
}
