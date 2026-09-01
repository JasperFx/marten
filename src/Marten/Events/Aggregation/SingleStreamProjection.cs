using System;
using System.Collections.Generic;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using JasperFx.Events.Projections.ContainerScoped;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Schema;
using Marten.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events.Aggregation;

/// <summary>
///     Base class for aggregating events by a stream using Marten-generated pattern matching
/// </summary>
/// <typeparam name="TDoc"></typeparam>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2091",
    Justification = "Class-level: generic type argument doesn't carry the DAM annotation of its target. The argument types flow in from StoreOptions / projection-registration on the caller side and are preserved by the trimmer at that boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public class SingleStreamProjection<TDoc, TId>:
    JasperFxSingleStreamProjectionBase<TDoc, TId, IDocumentOperations, IQuerySession>, IMartenAggregateProjection,
    IValidatedProjection<StoreOptions>, IMartenRegistrable where TDoc : notnull where TId : notnull
{
    // public override SubscriptionDescriptor Describe()
    // {
    //     return new SubscriptionDescriptor(this, SubscriptionType.SingleStreamProjection);
    // }

    /// <summary>
    ///     Advanced setting that enables a single stream projection to be stored as single-tenanted even
    ///     when the global event store has conjoined tenancy
    /// </summary>
    public bool IsGlobalWithinConjoinedTenancy { get; set; }

    void IMartenAggregateProjection.ConfigureAggregateMapping(DocumentMapping mapping, StoreOptions storeOptions)
    {
        mapping.UseVersionFromMatchingStream = true;
    }

    public override IEventSlicer BuildSlicer(IQuerySession session)
    {
        // This will address https://github.com/JasperFx/wolverine/issues/2053
        var isSingleTenanted = session.As<QuerySession>().Options.EventGraph.TenancyStyle == TenancyStyle.Single;

        // #5307: a global aggregate counts too, so that this agrees with FetchAsyncPlan, which has
        // always read `TenancyStyle == Single || GlobalAggregates.Contains(typeof(TDoc))`. Under
        // AddGlobalProjection the document is single-tenanted while the events stay conjoined, so
        // slicing per tenant would be slicing a stream that the fetch path treats as one.
        //
        // This is alignment, not a fix: no reachable corruption depended on it, and the reason is
        // structural rather than lucky. GlobalEventAppenderDecorator forces a global aggregate's
        // stream onto the default tenant at WRITE time, matching either on the stream's AggregateType
        // or on the event type being one the global projection includes -- which is exactly the set of
        // events this projection could ever apply. So every event that could reach an Apply is already
        // on one tenant by the time the slicer sees it, and the per-tenant split had nothing to split.
        // An event type the projection does NOT include can still land under another tenant, but it
        // lands in a group the projection ignores. Both shapes are pinned in
        // Bug_5307_global_aggregate_slicing_under_conjoined_tenancy.
        //
        // Setting it anyway means the two sites cannot drift into disagreeing again, and that the next
        // reader does not have to reconstruct the argument above to decide whether they are safe.
        return new TenantedEventSlicer<TDoc, TId>(new ByStream<TDoc, TId>())
        {
            ForceSingleTenancy = isSingleTenanted || IsGlobalWithinConjoinedTenancy
        };
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
                    var wrapper = ScopedAggregationWrapper.Build(s,
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
                    opts.Projections.Add((IProjectionSource<IDocumentOperations, IQuerySession>)projection, lifecycle);
                });
                break;

            case ServiceLifetime.Transient:
            case ServiceLifetime.Scoped:
                services.AddScoped<TConcrete>();
                services.ConfigureMarten<TStore>((s, opts) =>
                {
                    var wrapper = ScopedAggregationWrapper.Build(s,
                        typeof(TConcrete), typeof(TDoc), typeof(TId), typeof(IDocumentOperations),
                        typeof(IQuerySession));

                    wrapper.Lifecycle = lifecycle;
                    configure?.Invoke(wrapper);

                    opts.Projections.Add((IProjectionSource<IDocumentOperations, IQuerySession>)wrapper, lifecycle);
                });
                break;
        }
    }

    [JasperFxIgnore]
    public IEnumerable<string> ValidateConfiguration(StoreOptions options)
    {
        var mapping = options.Storage.FindMapping(typeof(TDoc)).Root.As<DocumentMapping>();

        foreach (var p in validateDocumentIdentity(options, mapping)) yield return p;

        if (options.Events.TenancyStyle != mapping.TenancyStyle)

        {
            if (Lifecycle != ProjectionLifecycle.Live && !IsGlobalWithinConjoinedTenancy &&
                options.Events.TenancyStyle != mapping.TenancyStyle)
            {
                yield return
                    $"Tenancy storage style mismatch between the events ({options.Events.TenancyStyle}) and the aggregate type {typeof(TDoc).FullNameInCode()} ({mapping.TenancyStyle})";
            }
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
        // Skip ID type validation for aggregates that use natural keys —
        // they intentionally have an ID type that differs from the stream identity
        if (NaturalKeyDefinition != null) yield break;

        var matches = IsIdTypeValidForStream(mapping.IdType, options, out var expectedType, out var valueTypeInfo);
        if (!matches)
        {
            yield return
                $"Id type mismatch. The stream identity type is {expectedType.NameInCode()} (or a strong typed identifier type that is convertible to {expectedType.NameInCode()}), but the aggregate document {typeof(TDoc).FullNameInCode()} id type is {mapping.IdType.NameInCode()}";
        }
    }
}
