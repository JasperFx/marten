using System;
using JasperFx.Core.Reflection;
using JasperFx.Events.Projections;
using JasperFx.Events.Projections.Composite;
using Marten.Events.Aggregation;
using Marten.Schema;

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
    /// Add a projection to be executed within this composite. The stage number is optional
    /// </summary>
    /// <param name="stageNumber">Optionally move the execution of this snapshot projection to a later stage. The default is 1</param>
    /// <typeparam name="T"></typeparam>
    public void Add<T>(int stageNumber = 1) where T : IProjectionSource<IDocumentOperations, IQuerySession>, new()
    {
        Add(new T(), stageNumber);
    }
}
