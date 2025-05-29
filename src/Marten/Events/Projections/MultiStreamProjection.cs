#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using JasperFx.Events.Projections.ContainerScoped;
using Marten.Events.Aggregation;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Marten.Events.Projections;

/// <summary>
///     Project a single document view across events that may span across
///     event streams in a user-defined grouping
/// </summary>
/// <typeparam name="TDoc"></typeparam>
/// <typeparam name="TId"></typeparam>
public abstract class MultiStreamProjection<TDoc, TId>: JasperFxMultiStreamProjectionBase<TDoc, TId, IDocumentOperations, IQuerySession>, IMartenAggregateProjection, IValidatedProjection<StoreOptions>, IMartenRegistrable where TDoc : notnull where TId : notnull
{
    protected MultiStreamProjection(): base()
    {
    }

    void IMartenAggregateProjection.ConfigureAggregateMapping(DocumentMapping mapping, StoreOptions storeOptions)
    {
        mapping.UseVersionFromMatchingStream = false;
        // Nothing right now.
    }

    /// <summary>
    ///     If more than 0 (the default), this is the maximum number of aggregates
    ///     that will be cached in a 2nd level, most recently used cache during async
    ///     projection. Use this to potentially improve async projection throughput
    /// </summary>
    [Obsolete("Prefer Options.CacheLimitPerTenant. This will be removed in Marten 9")]
    [JasperFxIgnore]
    public int CacheLimitPerTenant
    {
        get => Options.CacheLimitPerTenant;
        set => Options.CacheLimitPerTenant = value;
    }

    [JasperFxIgnore]
    public IEnumerable<string> ValidateConfiguration(StoreOptions options)
    {
        var mapping = options.Storage.FindMapping(typeof(TDoc)).Root.As<DocumentMapping>();

        if (mapping.IdType != typeof(TId))
        {
            yield return
                $"Id type mismatch. The projection identity type is {typeof(TId).FullNameInCode()}, but the aggregate document {typeof(TDoc).FullNameInCode()} id type is {mapping.IdType.NameInCode()}";
        }

        if (Lifecycle != ProjectionLifecycle.Live && options.Events.TenancyStyle == TenancyStyle.Conjoined && mapping.TenancyStyle == TenancyStyle.Single)
        {
            if (TenancyGrouping == TenancyGrouping.RespectTenant)
            {
                yield return $"Tenancy storage style mismatch between the events ({options.Events.TenancyStyle}) and the aggregate type {typeof(TDoc).FullNameInCode()} ({mapping.TenancyStyle}) but the {nameof(TenancyGrouping)} is {TenancyGrouping}. Set to {TenancyGrouping.AcrossTenants} to explicitly enable the grouping across tenants";
            }
        }

        if (Lifecycle != ProjectionLifecycle.Live && mapping.TenancyStyle == TenancyStyle.Conjoined &&
            options.Events.TenancyStyle == TenancyStyle.Single)
        {
            if (TenancyGrouping == TenancyGrouping.RespectTenant)
            {
                yield return $"Tenancy storage style mismatch between the events ({options.Events.TenancyStyle}) and the aggregate type {typeof(TDoc).FullNameInCode()} ({mapping.TenancyStyle}) but the {nameof(TenancyGrouping)} is {TenancyGrouping}. Set to {TenancyGrouping.AcrossTenants} to explicitly enable the grouping across tenants";
            }
        }

        if (mapping.DeleteStyle == DeleteStyle.SoftDelete && IsUsingConventionalMethods)
        {
            yield return
                "MultiStreamProjection cannot support aggregates that are soft-deleted with the conventional method approach. You will need to use an explicit workflow for this projection";
        }
    }

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
                    opts.Projections.Add((IProjectionSource<IDocumentOperations, IQuerySession>)projection, lifecycle);
                });
                break;

            case ServiceLifetime.Transient:
            case ServiceLifetime.Scoped:
                services.AddScoped<TConcrete>();
                services.ConfigureMarten((s, opts) =>
                {
                    var wrapper =
                        typeof(ScopedAggregationWrapper<,,,,>)
                            .CloseAndBuildAs<ProjectionBase>(s,
                                typeof(TConcrete), typeof(TDoc), typeof(TId), typeof(IDocumentOperations),
                                typeof(IQuerySession));

                    wrapper.Lifecycle = lifecycle;
                    configure?.Invoke(wrapper);

                    opts.Projections.Add((IProjectionSource<IDocumentOperations, IQuerySession>)wrapper, lifecycle);
                });
                break;
        }

    }

    public static void Register<TConcrete, TStore>(IServiceCollection services, ProjectionLifecycle lifecycle,
        ServiceLifetime lifetime, Action<ProjectionBase>? configure) where TConcrete : class where TStore : IDocumentStore
    {
        switch (lifetime)
        {
            case ServiceLifetime.Singleton:
                services.AddSingleton<TConcrete>();
                services.ConfigureMarten<TStore>((s, opts) =>
                {
                    var projection = s.GetRequiredService<TConcrete>();
                    opts.Projections.Add((IProjectionSource<IDocumentOperations, IQuerySession>)projection,
                        lifecycle);
                });
                break;

            case ServiceLifetime.Transient:
            case ServiceLifetime.Scoped:
                services.AddScoped<TConcrete>();
                services.ConfigureMarten<TStore>((s, opts) =>
                {
                    var wrapper =
                        typeof(ScopedAggregationWrapper<,,,,>)
                            .CloseAndBuildAs<ProjectionBase>(s,
                                typeof(TConcrete), typeof(TDoc), typeof(TId), typeof(IDocumentOperations),
                                typeof(IQuerySession));

                    wrapper.Lifecycle = lifecycle;
                    configure?.Invoke(wrapper);

                    opts.Projections.Add((IProjectionSource<IDocumentOperations, IQuerySession>)wrapper, lifecycle);
                });
                break;
        }
    }
}
