using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Descriptors;
using JasperFx.Events.Projections;
using JasperFx.Events.Projections.Composite;
using JasperFx.Events.Projections.ContainerScoped;
using JasperFx.Events.Subscriptions;
using Marten.Events.Aggregation;
using Marten.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Projections;

internal interface ICompositeProjectionServiceSource
{
    void AttachServiceProvider(IServiceProvider services);
}

public class CompositeProjection : CompositeProjection<IDocumentOperations, IQuerySession>
{
    private readonly StoreOptions _options;
    private IServiceProvider? _services;

    internal CompositeProjection(string name, StoreOptions options, ProjectionOptions parent) : base(name)
    {
        _options = options;
    }

    internal void AttachServiceProvider(IServiceProvider services)
    {
        _services = services;

        foreach (var source in AllProjections().OfType<ICompositeProjectionServiceSource>())
        {
            source.AttachServiceProvider(services);
        }
    }

    /// <summary>
    /// Add a snapshot (self-aggregating) projection to this composite.
    /// </summary>
    /// <param name="stageNumber">Optionally move the execution of this snapshot projection to a later stage. The default is 1</param>
    /// <typeparam name="T">The aggregated entity type</typeparam>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public MartenRegistry.DocumentMappingExpression<T> Snapshot<T>(int stageNumber = 1)
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
        source.Lifecycle = ProjectionLifecycle.Async;

        source.AssembleAndAssertValidity();

        StageFor(stageNumber).Add((IProjectionSource<IDocumentOperations, IQuerySession>)source);

        return expression;
    }

    /// <summary>
    /// Add a projection to be executed within this composite. The stage number is optional
    /// </summary>
    /// <param name="projection"></param>
    /// <param name="stageNumber">Optionally move the execution of this snapshot projection to a later stage. The default is 1</param>
    public void Add(IProjectionSource<IDocumentOperations, IQuerySession> projection, int stageNumber = 1)
    {
        if (projection is ProjectionBase b) b.AssembleAndAssertValidity();

        StageFor(stageNumber).Add(projection);
    }

    /// <summary>
    /// Add a custom IProjection implementation to be executed within this composite.
    /// The projection will be wrapped internally for composite-safe execution.
    /// </summary>
    /// <param name="projection">The custom IProjection implementation</param>
    /// <param name="stageNumber">Optionally move the execution to a later stage. The default is 1</param>
    public void Add(IProjection projection, int stageNumber = 1)
    {
        var wrapper = new CompositeIProjectionSource(projection);
        StageFor(stageNumber).Add(wrapper);
    }

    /// <summary>
    /// Add a projection to be executed within this composite. The stage number is optional
    /// </summary>
    /// <param name="stageNumber">Optionally move the execution of this snapshot projection to a later stage. The default is 1</param>
    /// <typeparam name="T"></typeparam>
    public void Add<T>(int stageNumber = 1) where T : IProjectionSource<IDocumentOperations, IQuerySession>, new()
    {
        Add(new T(), stageNumber);
    }

    /// <summary>
    /// Add a projection that requires services from the application's IoC container to this composite.
    /// </summary>
    /// <param name="lifetime">
    /// The IoC lifecycle for the projection instance. Note that the Transient lifetime will still be
    /// treated as Scoped.
    /// </param>
    /// <param name="stageNumber">Optionally move the execution to a later stage. The default is 1</param>
    /// <param name="configure">Optional configuration of the projection name, version, event filtering, and async execution</param>
    /// <typeparam name="TProjection">The projection type to add</typeparam>
    public void AddProjectionWithServices<TProjection>(ServiceLifetime lifetime = ServiceLifetime.Scoped,
        int stageNumber = 1, Action<ProjectionBase>? configure = null) where TProjection : class, IMartenRegistrable
    {
        var source = new CompositeProjectionWithServicesSource<TProjection>(lifetime, configure);

        if (_services != null)
        {
            source.AttachServiceProvider(_services);
        }

        StageFor(stageNumber).Add(source);
    }
}

internal class CompositeProjectionWithServicesSource<TProjection> :
    ProjectionBase,
    IProjectionSource<IDocumentOperations, IQuerySession>,
    ISubscriptionFactory<IDocumentOperations, IQuerySession>,
    IAggregateProjection,
    IMartenAggregateProjection,
    ICompositeProjectionServiceSource where TProjection : class, IMartenRegistrable
{
    private readonly Type? _aggregateType;
    private readonly Action<ProjectionBase>? _configure;
    private readonly Type? _identityType;
    private readonly ServiceLifetime _lifetime;
    private readonly AggregationScope _scope;
    private IProjectionSource<IDocumentOperations, IQuerySession>? _inner;
    private IServiceProvider? _services;

    public CompositeProjectionWithServicesSource(ServiceLifetime lifetime, Action<ProjectionBase>? configure)
    {
        _lifetime = lifetime;
        _configure = configure;
        Lifecycle = ProjectionLifecycle.Async;
        Name = typeof(TProjection).Name;
        Version = 1;

        if (typeof(TProjection).TryGetAttribute<ProjectionVersionAttribute>(out var att))
        {
            Version = att.Version;
        }

        _configure?.Invoke(this);

        if (tryFindAggregateTypes(typeof(TProjection), out var aggregateType, out var identityType, out var scope))
        {
            _aggregateType = aggregateType;
            _identityType = identityType;
            _scope = scope;
            RegisterPublishedType(aggregateType);
        }
    }

    public SubscriptionType Type => Source.Type;
    public ShardName[] ShardNames() => Source.ShardNames();
    public Type ImplementationType => Source.ImplementationType;
    public SubscriptionDescriptor Describe(IEventStore store) => Source.Describe(store);

    private IProjectionSource<IDocumentOperations, IQuerySession> Source
    {
        get
        {
            if (_inner != null)
            {
                return _inner;
            }

            if (_services == null)
            {
                throw new InvalidOperationException(
                    $"Projection {typeof(TProjection).FullNameInCode()} requires application services, but no IServiceProvider has been attached. Use this registration through AddMarten() so Marten can resolve the projection from DI.");
            }

            _inner = buildSource(_services);
            return _inner;
        }
    }

    public void AttachServiceProvider(IServiceProvider services)
    {
        _services = services;
        _inner ??= buildSource(services);
    }

    private IProjectionSource<IDocumentOperations, IQuerySession> buildSource(IServiceProvider services)
    {
        IProjectionSource<IDocumentOperations, IQuerySession> source;

        if (_lifetime == ServiceLifetime.Singleton)
        {
            source = buildSingletonSource(services);
        }
        else if (typeof(TProjection).CanBeCastTo<IProjection>() &&
                 !typeof(TProjection).CanBeCastTo<IProjectionSource<IDocumentOperations, IQuerySession>>())
        {
            source = typeof(CompositeScopedIProjectionSource<>)
                .CloseAndBuildAs<ProjectionBase>(services, typeof(TProjection))
                .As<IProjectionSource<IDocumentOperations, IQuerySession>>();
        }
        else if (_aggregateType != null && _identityType != null)
        {
            var projectionServices = new ProjectionActivatingServiceProvider<TProjection>(services);
            source = typeof(ScopedAggregationWrapper<,,,,>)
                .CloseAndBuildAs<ProjectionBase>(projectionServices, typeof(TProjection), _aggregateType, _identityType,
                    typeof(IDocumentOperations), typeof(IQuerySession))
                .As<IProjectionSource<IDocumentOperations, IQuerySession>>();
        }
        else
        {
            var projectionServices = new ProjectionActivatingServiceProvider<TProjection>(services);
            source = typeof(ScopedProjectionWrapper<,,>)
                .CloseAndBuildAs<ProjectionBase>(projectionServices, typeof(TProjection), typeof(IDocumentOperations),
                    typeof(IQuerySession))
                .As<IProjectionSource<IDocumentOperations, IQuerySession>>();
        }

        if (source is ProjectionBase projection)
        {
            projection.Lifecycle = ProjectionLifecycle.Async;
            _configure?.Invoke(projection);
            projection.Name = Name;
            projection.OverwriteVersion(Version);
        }

        return source;
    }

    private static IProjectionSource<IDocumentOperations, IQuerySession> buildSingletonSource(IServiceProvider services)
    {
        var projection = services.GetService<TProjection>() ??
                         ActivatorUtilities.CreateInstance<TProjection>(services);

        if (projection is IProjection martenProjection &&
            projection is not IProjectionSource<IDocumentOperations, IQuerySession>)
        {
            return new CompositeIProjectionSource(martenProjection);
        }

        return projection.As<IProjectionSource<IDocumentOperations, IQuerySession>>();
    }

    private static bool tryFindAggregateTypes(Type projectionType, out Type aggregateType, out Type identityType,
        out AggregationScope scope)
    {
        var type = projectionType;
        while (type != null && type != typeof(object))
        {
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(SingleStreamProjection<,>) ||
                    definition == typeof(MultiStreamProjection<,>))
                {
                    var arguments = type.GetGenericArguments();
                    aggregateType = arguments[0];
                    identityType = arguments[1];
                    scope = definition == typeof(SingleStreamProjection<,>)
                        ? AggregationScope.SingleStream
                        : AggregationScope.MultiStream;
                    return true;
                }
            }

            type = type.BaseType;
        }

        aggregateType = null!;
        identityType = null!;
        scope = default;
        return false;
    }

    Type IAggregateProjection.IdentityType => _identityType ?? typeof(void);
    Type IAggregateProjection.AggregateType => _aggregateType ?? typeof(void);
    AggregationScope IAggregateProjection.Scope => _scope;
    Type[] IAggregateProjection.AllEventTypes =>
        _aggregateType == null ? [] : Source.As<IAggregateProjection>().AllEventTypes;
    NaturalKeyDefinition? IAggregateProjection.NaturalKeyDefinition =>
        _aggregateType == null ? null : Source.As<IAggregateProjection>().NaturalKeyDefinition;

    void IMartenAggregateProjection.ConfigureAggregateMapping(DocumentMapping mapping, StoreOptions storeOptions)
    {
        if (_services != null)
        {
            using var scope = _services.CreateScope();
            var projection = scope.ServiceProvider.GetService<TProjection>() ??
                             ActivatorUtilities.CreateInstance<TProjection>(scope.ServiceProvider);

            if (projection is IMartenAggregateProjection martenAggregateProjection)
            {
                martenAggregateProjection.ConfigureAggregateMapping(mapping, storeOptions);
                return;
            }
        }

        if (_scope == AggregationScope.SingleStream)
        {
            mapping.UseVersionFromMatchingStream = true;
        }
    }

    public IReadOnlyList<AsyncShard<IDocumentOperations, IQuerySession>> Shards() => Source.Shards();

    public bool TryBuildReplayExecutor(IEventStore<IDocumentOperations, IQuerySession> store, IEventDatabase database,
        [NotNullWhen(true)] out IReplayExecutor? executor)
    {
        return Source.TryBuildReplayExecutor(store, database, out executor);
    }

    public IInlineProjection<IDocumentOperations> BuildForInline()
    {
        throw new NotSupportedException("Composite projections do not support inline execution");
    }

    public ISubscriptionExecution BuildExecution(IEventStore<IDocumentOperations, IQuerySession> store,
        IEventDatabase database, ILoggerFactory loggerFactory, ShardName shardName)
    {
        return Source.As<ISubscriptionFactory<IDocumentOperations, IQuerySession>>()
            .BuildExecution(store, database, loggerFactory, shardName);
    }

    public ISubscriptionExecution BuildExecution(IEventStore<IDocumentOperations, IQuerySession> store,
        IEventDatabase database, ILogger logger, ShardName shardName)
    {
        return Source.As<ISubscriptionFactory<IDocumentOperations, IQuerySession>>()
            .BuildExecution(store, database, logger, shardName);
    }
}

internal class CompositeScopedIProjectionSource<TProjection> :
    ProjectionBase,
    IProjectionSource<IDocumentOperations, IQuerySession>,
    ISubscriptionFactory<IDocumentOperations, IQuerySession> where TProjection : class, IProjection
{
    private readonly IServiceProvider _services;

    public CompositeScopedIProjectionSource(IServiceProvider services)
    {
        _services = services;
        Lifecycle = ProjectionLifecycle.Async;
        Name = typeof(TProjection).Name;
        Version = 1;
        if (typeof(TProjection).TryGetAttribute<ProjectionVersionAttribute>(out var att))
        {
            Version = att.Version;
        }
    }

    public SubscriptionType Type => SubscriptionType.EventProjection;
    public ShardName[] ShardNames() => [new ShardName(Name, ShardName.All, Version)];
    public Type ImplementationType => typeof(TProjection);
    public SubscriptionDescriptor Describe(IEventStore store) => new(this, store);

    public IReadOnlyList<AsyncShard<IDocumentOperations, IQuerySession>> Shards()
    {
        return
        [
            new AsyncShard<IDocumentOperations, IQuerySession>(Options, ShardRole.Projection,
                new ShardName(Name, "All", Version), this, this)
        ];
    }

    public bool TryBuildReplayExecutor(IEventStore<IDocumentOperations, IQuerySession> store, IEventDatabase database,
        [NotNullWhen(true)] out IReplayExecutor? executor)
    {
        executor = default;
        return false;
    }

    public IInlineProjection<IDocumentOperations> BuildForInline()
    {
        throw new NotSupportedException("CompositeScopedIProjectionSource does not support inline execution");
    }

    public ISubscriptionExecution BuildExecution(IEventStore<IDocumentOperations, IQuerySession> store,
        IEventDatabase database, ILoggerFactory loggerFactory, ShardName shardName)
    {
        return new CompositeScopedIProjectionExecution<TProjection>(_services, shardName);
    }

    public ISubscriptionExecution BuildExecution(IEventStore<IDocumentOperations, IQuerySession> store,
        IEventDatabase database, ILogger logger, ShardName shardName)
    {
        return new CompositeScopedIProjectionExecution<TProjection>(_services, shardName);
    }
}

internal class CompositeScopedIProjectionExecution<TProjection> : ISubscriptionExecution where TProjection : class, IProjection
{
    private readonly IServiceProvider _services;

    public CompositeScopedIProjectionExecution(IServiceProvider services, ShardName shardName)
    {
        _services = services;
        ShardName = shardName;
    }

    public ShardName ShardName { get; }
    public ShardExecutionMode Mode { get; set; }

    public async Task ProcessRangeAsync(EventRange range)
    {
        var batch = range.ActiveBatch as IProjectionBatch<IDocumentOperations, IQuerySession>;
        if (batch == null) return;

        var groups = range.Events.GroupBy(x => x.TenantId).ToArray();
        foreach (var group in groups)
        {
            using var scope = _services.CreateScope();
            var projection = ActivatorUtilities.GetServiceOrCreateInstance<TProjection>(scope.ServiceProvider);

            await using var session = batch.SessionForTenant(group.Key);
            await projection.ApplyAsync(session, group.ToList(), CancellationToken.None).ConfigureAwait(false);
        }
    }

    public ValueTask EnqueueAsync(EventPage page, ISubscriptionAgent subscriptionAgent) => new();
    public Task StopAndDrainAsync(CancellationToken token) => Task.CompletedTask;
    public Task HardStopAsync() => Task.CompletedTask;

    public bool TryBuildReplayExecutor([NotNullWhen(true)] out IReplayExecutor? executor)
    {
        executor = default;
        return false;
    }

    public Task ProcessImmediatelyAsync(SubscriptionAgent subscriptionAgent, EventPage events,
        CancellationToken cancellation) => Task.CompletedTask;

    public bool TryGetAggregateCache<TId, TDoc>([NotNullWhen(true)] out IAggregateCaching<TId, TDoc>? caching)
    {
        caching = null;
        return false;
    }

    public ValueTask DisposeAsync() => new();
}

internal class ProjectionActivatingServiceProvider<TProjection> : IServiceProvider, IServiceScopeFactory
    where TProjection : class
{
    private readonly IServiceProvider _inner;

    public ProjectionActivatingServiceProvider(IServiceProvider inner)
    {
        _inner = inner;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceScopeFactory))
        {
            return this;
        }

        if (serviceType == typeof(TProjection))
        {
            return _inner.GetService(serviceType) ?? ActivatorUtilities.CreateInstance(_inner, serviceType);
        }

        return _inner.GetService(serviceType);
    }

    public IServiceScope CreateScope()
    {
        return new ProjectionActivatingServiceScope<TProjection>(_inner.CreateScope());
    }
}

internal class ProjectionActivatingServiceScope<TProjection> : IServiceScope where TProjection : class
{
    private readonly IServiceScope _inner;

    public ProjectionActivatingServiceScope(IServiceScope inner)
    {
        _inner = inner;
        ServiceProvider = new ProjectionActivatingServiceProvider<TProjection>(inner.ServiceProvider);
    }

    public IServiceProvider ServiceProvider { get; }

    public void Dispose()
    {
        _inner.Dispose();
    }
}

/// <summary>
/// Wraps an IProjection for use inside a CompositeProjection, providing composite-safe
/// batch lifecycle management. Unlike ProjectionWrapper which uses ProjectionExecution
/// (and unconditionally disposes shared batches), this wrapper only disposes batches it owns.
/// </summary>
internal class CompositeIProjectionSource :
    ProjectionBase,
    IProjectionSource<IDocumentOperations, IQuerySession>,
    ISubscriptionFactory<IDocumentOperations, IQuerySession>
{
    private readonly IProjection _projection;

    public CompositeIProjectionSource(IProjection projection)
    {
        _projection = projection;
        Lifecycle = ProjectionLifecycle.Async;
        Name = projection.GetType().Name;
        Version = 1;
        if (_projection.GetType().TryGetAttribute<ProjectionVersionAttribute>(out var att))
        {
            Version = att.Version;
        }
    }

    public SubscriptionType Type => SubscriptionType.EventProjection;
    public ShardName[] ShardNames() => [new ShardName(Name, ShardName.All, Version)];
    public Type ImplementationType => _projection.GetType();
    public SubscriptionDescriptor Describe(IEventStore store) => new(this, store);

    public IReadOnlyList<AsyncShard<IDocumentOperations, IQuerySession>> Shards()
    {
        return
        [
            new AsyncShard<IDocumentOperations, IQuerySession>(Options, ShardRole.Projection,
                new ShardName(Name, "All", Version), this, this)
        ];
    }

    public bool TryBuildReplayExecutor(IEventStore<IDocumentOperations, IQuerySession> store, IEventDatabase database,
        [NotNullWhen(true)] out IReplayExecutor? executor)
    {
        executor = default;
        return false;
    }

    public IInlineProjection<IDocumentOperations> BuildForInline()
    {
        throw new NotSupportedException("CompositeIProjectionSource does not support inline execution");
    }

    public ISubscriptionExecution BuildExecution(IEventStore<IDocumentOperations, IQuerySession> store,
        IEventDatabase database, ILoggerFactory loggerFactory, ShardName shardName)
    {
        return new CompositeIProjectionExecution(_projection, shardName);
    }

    public ISubscriptionExecution BuildExecution(IEventStore<IDocumentOperations, IQuerySession> store,
        IEventDatabase database, ILogger logger, ShardName shardName)
    {
        return new CompositeIProjectionExecution(_projection, shardName);
    }
}

/// <summary>
/// A lightweight ISubscriptionExecution for IProjection instances running inside a composite.
/// Does NOT dispose the shared batch — the composite manages batch lifecycle.
/// </summary>
internal class CompositeIProjectionExecution : ISubscriptionExecution
{
    private readonly IProjection _projection;

    public CompositeIProjectionExecution(IProjection projection, ShardName shardName)
    {
        _projection = projection;
        ShardName = shardName;
    }

    public ShardName ShardName { get; }
    public ShardExecutionMode Mode { get; set; }

    public async Task ProcessRangeAsync(EventRange range)
    {
        var batch = range.ActiveBatch as IProjectionBatch<IDocumentOperations, IQuerySession>;
        if (batch == null) return;

        var groups = range.Events.GroupBy(x => x.TenantId).ToArray();
        foreach (var group in groups)
        {
            await using var session = batch.SessionForTenant(group.Key);
            await _projection.ApplyAsync(session, group.ToList(), CancellationToken.None).ConfigureAwait(false);
        }
    }

    public ValueTask EnqueueAsync(EventPage page, ISubscriptionAgent subscriptionAgent) => new();
    public Task StopAndDrainAsync(CancellationToken token) => Task.CompletedTask;
    public Task HardStopAsync() => Task.CompletedTask;

    public bool TryBuildReplayExecutor([NotNullWhen(true)] out IReplayExecutor? executor)
    {
        executor = default;
        return false;
    }

    public Task ProcessImmediatelyAsync(SubscriptionAgent subscriptionAgent, EventPage events,
        CancellationToken cancellation) => Task.CompletedTask;

    public bool TryGetAggregateCache<TId, TDoc>([NotNullWhen(true)] out IAggregateCaching<TId, TDoc>? caching)
    {
        caching = null;
        return false;
    }

    public ValueTask DisposeAsync() => new();
}
