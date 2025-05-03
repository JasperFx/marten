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

namespace Marten.Events.Projections;

/// <summary>
///     This is the "do anything" projection type
/// </summary>
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
