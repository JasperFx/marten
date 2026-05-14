using System;
using System.Collections.Generic;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events.Daemon;
using JasperFx.Events.Descriptors;
using JasperFx.Events.Projections;
using JasperFx.Events.Projections.ContainerScoped;
using Marten.Storage;
using Microsoft.Extensions.DependencyInjection;
using Weasel.Core;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events.Projections;

/// <summary>
///     This is the "do anything" projection type
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2091",
    Justification = "Class-level: generic type argument doesn't carry the DAM annotation of its target. The argument types flow in from StoreOptions / projection-registration on the caller side and are preserved by the trimmer at that boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public abstract class EventProjection: JasperFxEventProjectionBase<IDocumentOperations, IQuerySession>,
    IValidatedProjection<StoreOptions>, IProjectionSchemaSource, IMartenRegistrable
{
    /// <summary>
    ///     Use to register additional or custom schema objects like database tables that
    ///     will be used by this projection. Originally meant to support projecting to flat
    ///     tables
    /// </summary>
    public IList<ISchemaObject> SchemaObjects { get; } = new List<ISchemaObject>();

    public static void Register<TConcrete>(IServiceCollection services, ProjectionLifecycle lifecycle,
        ServiceLifetime lifetime, Action<ProjectionBase>? configure) where TConcrete : class
    {
        switch (lifetime)
        {
            case ServiceLifetime.Singleton:
                services.AddSingleton<TConcrete>();
                services.ConfigureMarten((s, opts) =>
                {
                    var projection = s.GetRequiredService<TConcrete>().As<EventProjection>();
                    configure?.Invoke(projection);

                    opts.Projections.Add(projection, lifecycle);
                });
                break;

            case ServiceLifetime.Transient:
            case ServiceLifetime.Scoped:
                services.AddScoped<TConcrete>();
                services.ConfigureMarten((s, opts) =>
                {
                    var wrapper = typeof(ScopedProjectionWrapper<,,>).CloseAndBuildAs<ProjectionBase>(s,
                        typeof(TConcrete), typeof(IDocumentOperations), typeof(IQuerySession));

                    wrapper.Lifecycle = lifecycle;
                    configure?.Invoke(wrapper);

                    opts.Projections.Add((IProjectionSource<IDocumentOperations, IQuerySession>)wrapper, lifecycle);
                });
                break;
        }
    }

    public static void Register<TConcrete, TStore>(IServiceCollection services, ProjectionLifecycle lifecycle,
        ServiceLifetime lifetime, Action<ProjectionBase>? configure)
        where TStore : IDocumentStore where TConcrete : class
    {
        switch (lifetime)
        {
            case ServiceLifetime.Singleton:
                services.AddSingleton<TConcrete>();
                services.ConfigureMarten<TStore>((s, opts) =>
                {
                    var projection = s.GetRequiredService<TConcrete>();
                    var wrapper =
                        new ProjectionWrapper<IDocumentOperations, IQuerySession>((IProjection)projection, lifecycle);
                    configure?.Invoke(wrapper);

                    opts.Projections.Add(wrapper, lifecycle);
                });
                break;

            case ServiceLifetime.Transient:
            case ServiceLifetime.Scoped:
                services.AddScoped<TConcrete>();
                services.ConfigureMarten<TStore>((s, opts) =>
                {
                    var wrapper = typeof(ScopedProjectionWrapper<,,>).CloseAndBuildAs<ProjectionBase>(s,
                        typeof(TConcrete), typeof(IDocumentOperations), typeof(IQuerySession));

                    wrapper.Lifecycle = lifecycle;
                    configure?.Invoke(wrapper);

                    opts.Projections.Add((IProjectionSource<IDocumentOperations, IQuerySession>)wrapper, lifecycle);
                });
                break;
        }
    }

    IEnumerable<ISchemaObject> IProjectionSchemaSource.CreateSchemaObjects(EventGraph events)
    {
        return SchemaObjects;
    }

    [JasperFxIgnore]
    public IEnumerable<string> ValidateConfiguration(StoreOptions options)
    {
        AssembleAndAssertValidity();

        return ArraySegment<string>.Empty;
    }

    // TODO upstream change
    protected sealed override void storeEntity<T>(IDocumentOperations ops, T entity)
    {
        ops.Store(entity);
    }

    public bool TryBuildReplayExecutor(DocumentStore store, IMartenDatabase database, out IReplayExecutor? executor)
    {
        executor = default;
        return false;
    }
}
