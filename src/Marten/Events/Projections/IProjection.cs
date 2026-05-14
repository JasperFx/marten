#nullable enable
using System;
using JasperFx.Core.Reflection;
using JasperFx.Events.Projections;
using JasperFx.Events.Projections.ContainerScoped;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events.Projections;

#region sample_iprojection

/// <summary>
///     Interface for all event projections
///     IProjection implementations define the projection type and handle its projection document lifecycle
///     Optimized for inline usage
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2091",
    Justification = "Class-level: generic type argument doesn't carry the DAM annotation of its target. The argument types flow in from StoreOptions / projection-registration on the caller side and are preserved by the trimmer at that boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
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


