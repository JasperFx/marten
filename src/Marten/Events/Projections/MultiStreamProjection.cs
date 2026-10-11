#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using Microsoft.Extensions.Logging;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using JasperFx.Events.Projections.ContainerScoped;
using Marten.Events.Aggregation;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events.Projections;

/// <summary>
///     Project a single document view across events that may span across
///     event streams in a user-defined grouping
/// </summary>
/// <typeparam name="TDoc"></typeparam>
/// <typeparam name="TId"></typeparam>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2091",
    Justification = "Class-level: generic type argument doesn't carry the DAM annotation of its target. The argument types flow in from StoreOptions / projection-registration on the caller side and are preserved by the trimmer at that boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public abstract class MultiStreamProjection<TDoc, TId>: JasperFxMultiStreamProjectionBase<TDoc, TId, IDocumentOperations, IQuerySession>, IMartenAggregateProjection, IValidatedProjection<StoreOptions>, IMartenRegistrable,
    IProjectionBackfillSource<IDocumentOperations, IQuerySession>, IProjectionWithBackfills where TDoc : notnull where TId : notnull
{
    private readonly ProjectionBackfills<TDoc, TId> _backfills = new();

    protected MultiStreamProjection(): base()
    {
    }

    /// <summary>
    ///     Fill a member added to <typeparamref name="TDoc" /> without a version bump. See <see cref="ProjectionBackfill{TDoc,TId}" />.
    /// </summary>
    [JasperFxIgnore]
    public ProjectionBackfill<TDoc, TId> Backfill(string name) => _backfills.Add(name);

    bool IProjectionWithBackfills.HasBackfills => _backfills.All.Count > 0;

    Type IProjectionWithBackfills.BackfilledDocumentType => typeof(TDoc);

    bool IProjectionBackfillSource<IDocumentOperations, IQuerySession>.TryBuildBackfillExecutor(
        IEventStore<IDocumentOperations, IQuerySession> store, IEventDatabase database, ShardName shardName,
        ILogger logger, [NotNullWhen(true)] out IBackfillExecutor? executor)
    {
        return _backfills.TryBuildExecutor(store, database, shardName, logger, this, out executor);
    }

    void IMartenAggregateProjection.ConfigureAggregateMapping(DocumentMapping mapping, StoreOptions storeOptions)
    {
        mapping.UseVersionFromMatchingStream = false;
        // Nothing right now.
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

        foreach (var message in _backfills.Validate(this)) yield return message;
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
}
