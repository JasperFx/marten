using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using JasperFx.Events.Subscriptions;
using Marten.Events.Aggregation;
using Marten.Events.Fetching;
using Marten.Internal.OpenTelemetry;
using Marten.Schema;
using Marten.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events.Projections;

/// <summary>
///     Used to register projections with Marten
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public class ProjectionOptions: ProjectionGraph<IProjection, IDocumentOperations, IQuerySession>
{
    internal readonly IFetchPlanner[] _builtInPlanners =
        [new NaturalKeyFetchPlanner(), new InlineFetchPlanner(), new AsyncFetchPlanner(), new LiveFetchPlanner()];

    private readonly StoreOptions _options;

    /// <summary>
    ///     Register session listeners that will ONLY be applied within the asynchronous daemon updates.
    /// </summary>
    public readonly List<IChangeListener> AsyncListeners = new();

    internal ProjectionOptions(StoreOptions options): base(options.EventGraph, "marten")
    {
        _options = options;

        ActivitySource = MartenTracing.ActivitySource;
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

    /// <summary>
    ///     Controls whether DocumentStore construction scans loaded assemblies for
    ///     <c>[GeneratedEvolverAttribute]</c> markers emitted by Marten's source generator
    ///     (Lens 1 cold-start optimization for #4294). Default is <see langword="true"/>;
    ///     apps that do not use source-generated aggregate evolvers can set this to
    ///     <see langword="false"/> to skip the discovery walk and the AppDomain
    ///     assembly enumeration that drives it. Has no effect on runtime behavior
    ///     (evolver dispatch is unrelated); only affects construction-time cost.
    /// </summary>
    public bool DiscoverGeneratedEvolversOnStartup { get; set; } = true;

    /// <summary>
    ///     <para>
    ///     Experimental opt-in for #4685 (rebuild write-side performance): when a projection
    ///     <b>rebuild</b> batch's document writes are pure inserts (user projection code calling
    ///     <c>IDocumentOperations.Insert(...)</c>, e.g. an event-to-row transform projection),
    ///     Marten buffers those inserts and flushes them through PostgreSQL's binary
    ///     <c>COPY</c> protocol (the same BulkWriter machinery behind
    ///     <c>IDocumentStore.BulkInsertAsync</c>) instead of the per-row INSERT command path.
    ///     Binary COPY is typically several times faster for bulk loads.
    ///     </para>
    ///     <para>
    ///     Safety: the COPY dispatch degrades gracefully. If any non-insert document operation
    ///     (update, upsert, patch, delete, ad-hoc SQL) arrives in the same batch, the buffered
    ///     inserts drain back onto the ordinary per-row command path in their original order and
    ///     the batch executes exactly as it would without this flag. Continuous (non-rebuild)
    ///     projection execution is never affected. Everything still commits in the one batch
    ///     transaction, so a failed rebuild cannot leak partially-copied rows.
    ///     </para>
    ///     <para>Default is <see langword="false"/>.</para>
    /// </summary>
    public bool RebuildWithBulkCopy { get; set; }

    // Snapshot of AppDomain.CurrentDomain.GetAssemblies() taken on first construction
    // of any DocumentStore in the process. The audit row #4294/Lens-1/#1 calls this out
    // as a top-fix candidate — multiple DocumentStore instances (multi-database tenancy)
    // each pay for re-enumerating the full loaded-assembly set otherwise. Trade-off:
    // assemblies loaded *after* the first DocumentStore construction won't be scanned
    // for evolvers; in practice the loaded-assembly set is stable once host startup
    // completes, so this matches typical evolver-discovery semantics. Race on first
    // assignment is benign (both racers get a valid array; the late writer's array
    // is discarded).
    private static Assembly[]? _cachedLoadedAssemblies;

    internal static Assembly[] CachedLoadedAssemblies =>
        _cachedLoadedAssemblies ??= AppDomain.CurrentDomain.GetAssemblies();

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

    internal void AttachServiceProvider(IServiceProvider services)
    {
        foreach (var composite in All.OfType<CompositeProjection>())
        {
            composite.AttachServiceProvider(services);
        }
    }

    internal IInlineProjection<IDocumentOperations>[] BuildInlineProjections(DocumentStore store)
    {
        var projections = All
            .Where(x => x.Lifecycle == ProjectionLifecycle.Inline)
            .Select(x => x.BuildForInline())
            .ToList();

        // Auto-register NaturalKeyProjection for any aggregate that has a NaturalKeyDefinition
        foreach (var aggregate in All.OfType<IAggregateProjection>())
        {
            if (aggregate.NaturalKeyDefinition != null)
            {
                projections.Add(new NaturalKeyProjection(_options.EventGraph, aggregate.NaturalKeyDefinition));
            }
        }

        return projections.ToArray();
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
        Action<AsyncOptions>? asyncConfiguration = null
    )
    {
        var expression = SingleStreamProjection<T>(ProjectionLifecycle.Live, null, asyncConfiguration);

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
        Action<AsyncOptions>? asyncConfiguration = null
    )
    {
        return SingleStreamProjection<T>(lifecycle.Map(), null, asyncConfiguration);
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
        Action<AsyncOptions>? asyncConfiguration = null
    )
    {
        return SingleStreamProjection<T>(lifecycle.Map(), configureProjection, asyncConfiguration);
    }

    internal MartenRegistry.DocumentMappingExpression<T> SingleStreamProjection<T>(
        ProjectionLifecycle lifecycle,
        Action<ProjectionBase>? configureProjection = null,
        Action<AsyncOptions>? asyncConfiguration = null
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

    /// <summary>
    /// Adds a single stream projection to this Marten application and marks the aggregate type
    /// as being globally tenanted. Use this option *if* you have an otherwise multi-tenanted event store,
    /// but a particular type of stream and aggregate should be stored globally (single tenanted).
    ///
    /// This will force Marten to append any events that it knows are related to this projection type
    /// to the default tenant id regardless of how the session is opened or the current tenant id
    /// </summary>
    /// <param name="lifecycle"></param>
    /// <param name="asyncConfiguration"></param>
    /// <typeparam name="T"></typeparam>
    public void AddGlobalProjection<TDoc, TId>(SingleStreamProjection<TDoc, TId> projection, ProjectionLifecycle lifecycle)
    {
        _options.EventGraph.GlobalAggregates.Add(typeof(TDoc));
        Add(projection, lifecycle);
        projection.IsGlobalWithinConjoinedTenancy = true;

        // Override the tenancy here
        _options.Schema.For<TDoc>().SingleTenanted();
    }

    /// <summary>
    /// Find an existing CompositeProjection with the supplied name (case insensitive search!)
    /// This method will create a new projection with this name if one does not already exist
    /// </summary>
    /// <param name="name"></param>
    /// <param name="configure">Optionally configure the CompositeProjection</param>
    /// <returns></returns>
    public void CompositeProjectionFor(string name, Action<CompositeProjection>? configure = null)
    {
        var projection = All.FirstOrDefault(x => x.Name.EqualsIgnoreCase(name));
        if (projection != null && projection is not CompositeProjection)
        {
            throw new ArgumentOutOfRangeException(nameof(name),
                "Conflicts with a registered projection of type " + projection.GetType().FullNameInCode());
        }

        var composite = projection as CompositeProjection;
        if (composite == null)
        {
            composite = new CompositeProjection(name, _options, this);
            Add(composite, ProjectionLifecycle.Async);
        }

        configure?.Invoke(composite);

        foreach (var projectionSource in composite.AllProjections())
        {
            projectionSource.OverwriteVersion(composite.Version);
        }
    }

    internal void AttachLogging(ILoggerFactory loggerFactory)
    {
        foreach (var hasLogger in All.OfType<IHasLogger>())
        {
            hasLogger.AttachLogger(loggerFactory);
        }
    }

    /// <summary>
    /// Find a registered natural key definition for the given aggregate type, if any.
    /// </summary>
    public NaturalKeyDefinition? FindNaturalKeyDefinition(Type aggregateType)
    {
        if (TryFindAggregate(aggregateType, out var projection))
        {
            return projection.NaturalKeyDefinition;
        }

        return null;
    }
}
