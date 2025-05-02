#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using JasperFx.Events.Projections.ContainerScoped;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Marten.Events.Aggregation;

/// <summary>
///     Base class for aggregating events by a stream using Marten-generated pattern matching
/// </summary>
/// <typeparam name="TDoc"></typeparam>
public class SingleStreamProjection<TDoc, TId>:
    JasperFxSingleStreamProjectionBase<TDoc, TId, IDocumentOperations, IQuerySession>, IMartenAggregateProjection,
    IValidatedProjection<StoreOptions>, IMartenRegistrable
{
    // public override SubscriptionDescriptor Describe()
    // {
    //     return new SubscriptionDescriptor(this, SubscriptionType.SingleStreamProjection);
    // }

    public SingleStreamProjection(): base()
    {
    }


    void IMartenAggregateProjection.ConfigureAggregateMapping(DocumentMapping mapping, StoreOptions storeOptions)
    {
        mapping.UseVersionFromMatchingStream = true;
    }

    [JasperFxIgnore]
    public IEnumerable<string> ValidateConfiguration(StoreOptions options)
    {
        var mapping = options.Storage.FindMapping(typeof(TDoc)).Root.As<DocumentMapping>();

        foreach (var p in validateDocumentIdentity(options, mapping)) yield return p;

        if (options.Events.TenancyStyle != mapping.TenancyStyle
            && (options.Events.TenancyStyle == TenancyStyle.Single
                || (options.Events is
                        { TenancyStyle: TenancyStyle.Conjoined, EnableGlobalProjectionsForConjoinedTenancy: false }
                    && Lifecycle != ProjectionLifecycle.Live))
           )
        {
            yield return
                $"Tenancy storage style mismatch between the events ({options.Events.TenancyStyle}) and the aggregate type {typeof(TDoc).FullNameInCode()} ({mapping.TenancyStyle})";
        }

        if (mapping.DeleteStyle == DeleteStyle.SoftDelete && IsUsingConventionalMethods)
        {
            yield return
                "SingleStreamProjection cannot support aggregates that are soft-deleted with the conventional method approach. You will need to use an explicit workflow for this projection";
        }
    }

    internal bool IsIdTypeValidForStream(Type idType, StoreOptions options, out Type expectedType,
        out ValueTypeInfo? valueType)
    {
        valueType = default;
        expectedType = options.Events.StreamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);
        if (idType == expectedType)
        {
            return true;
        }

        valueType = options.TryFindValueType(idType);
        if (valueType == null)
        {
            return false;
        }

        return valueType.SimpleType == expectedType;
    }

    protected IEnumerable<string> validateDocumentIdentity(StoreOptions options,
        DocumentMapping mapping)
    {
        var matches = IsIdTypeValidForStream(mapping.IdType, options, out var expectedType, out var valueTypeInfo);
        if (!matches)
        {
            yield return
                $"Id type mismatch. The stream identity type is {expectedType.NameInCode()} (or a strong typed identifier type that is convertible to {expectedType.NameInCode()}), but the aggregate document {typeof(TDoc).FullNameInCode()} id type is {mapping.IdType.NameInCode()}";
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

                    if (projection is ProjectionBase basic)
                    {
                        configure?.Invoke(basic);
                    }

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
        ServiceLifetime lifetime, Action<ProjectionBase>? configure) where TStore : IDocumentStore where TConcrete : class
    {
        switch (lifetime)
        {
            case ServiceLifetime.Singleton:
                services.AddSingleton<TConcrete>();
                services.ConfigureMarten<TStore>((s, opts) =>
                {
                    var projection = s.GetRequiredService<TConcrete>();
                    opts.Projections.Add((IProjectionSource<IDocumentOperations, IQuerySession>)projection, lifecycle);
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
