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
using JasperFx.Events.Subscriptions;
using Marten.Events.Aggregation;
using Marten.Schema;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Projections;

public class CompositeProjection : CompositeProjection<IDocumentOperations, IQuerySession>
{
    private readonly StoreOptions _options;

    internal CompositeProjection(string name, StoreOptions options, ProjectionOptions parent) : base(name)
    {
        _options = options;
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
