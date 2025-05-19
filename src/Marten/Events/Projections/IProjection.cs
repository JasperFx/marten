#nullable enable
using System;
using JasperFx.Core.Reflection;
using JasperFx.Events.Projections;
using JasperFx.Events.Projections.ContainerScoped;
using Microsoft.Extensions.DependencyInjection;

namespace Marten.Events.Projections;

#region sample_IProjection

/// <summary>
///     Interface for all event projections
///     IProjection implementations define the projection type and handle its projection document lifecycle
///     Optimized for inline usage
/// </summary>
public interface IProjection: IJasperFxProjection<IDocumentOperations>, IMartenRegistrable
#endregion
{
    // Ignore this, ugly Marten internals......
    static void IMartenRegistrable.Register<TConcrete>(IServiceCollection services, ProjectionLifecycle lifecycle,
        ServiceLifetime lifetime, Action<ProjectionBase>? configure)
    {
        switch (lifetime)
        {
            case ServiceLifetime.Singleton:
                services.AddSingleton<TConcrete>();
                services.ConfigureMarten((s, opts) =>
                {
                    var projection = s.GetRequiredService<TConcrete>();
                    var wrapper = new ProjectionWrapper<IDocumentOperations, IQuerySession>((IProjection)projection, lifecycle);
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

    static void IMartenRegistrable.Register<TConcrete, TStore>(IServiceCollection services, ProjectionLifecycle lifecycle,
        ServiceLifetime lifetime, Action<ProjectionBase>? configure)
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
}


