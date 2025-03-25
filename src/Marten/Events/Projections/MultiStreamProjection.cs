#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Storage;
using Npgsql;

namespace Marten.Events.Projections;

/// <summary>
///     Project a single document view across events that may span across
///     event streams in a user-defined grouping
/// </summary>
/// <typeparam name="TDoc"></typeparam>
/// <typeparam name="TId"></typeparam>
public abstract class MultiStreamProjection<TDoc, TId>: JasperFxMultiStreamProjectionBase<TDoc, TId, IDocumentOperations, IQuerySession>, IMartenAggregateProjection, IValidatedProjection<StoreOptions>
{
    // TODO -- put the exception types in a constant somewhere
    protected MultiStreamProjection(): base([typeof(NpgsqlException), typeof(MartenCommandException)])
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
    public int CacheLimitPerTenant
    {
        get => Options.CacheLimitPerTenant;
        set => Options.CacheLimitPerTenant = value;
    }

    // TODO -- need to add this all the way back in JasperFx.Events
    // public override SubscriptionDescriptor Describe()
    // {
    //     return new SubscriptionDescriptor(this, SubscriptionType.MultiStreamProjection);
    // }

    public IEnumerable<string> ValidateConfiguration(StoreOptions options)
    {
        var mapping = options.Storage.FindMapping(typeof(TDoc)).Root.As<DocumentMapping>();

        if (mapping.IdType != typeof(TId))
        {
            yield return
                $"Id type mismatch. The projection identity type is {typeof(TId).FullNameInCode()}, but the aggregate document {typeof(TDoc).FullNameInCode()} id type is {mapping.IdType.NameInCode()}";
        }

        // TODO -- revisit this with
        if (options.Events.TenancyStyle != mapping.TenancyStyle
            && (options.Events.TenancyStyle == TenancyStyle.Single
                || options.Events is
                    { TenancyStyle: TenancyStyle.Conjoined, EnableGlobalProjectionsForConjoinedTenancy: false }
                && Lifecycle != ProjectionLifecycle.Live)
           )
        {
            yield return
                $"Tenancy storage style mismatch between the events ({options.Events.TenancyStyle}) and the aggregate type {typeof(TDoc).FullNameInCode()} ({mapping.TenancyStyle})";
        }

        if (mapping.DeleteStyle == DeleteStyle.SoftDelete && IsUsingConventionalMethods)
        {
            yield return
                "MultiStreamProjection cannot support aggregates that are soft-deleted with the conventional method approach. You will need to use an explicit workflow for this projection";
        }
    }

}
