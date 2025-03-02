#nullable enable
using System;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten.Events.Aggregation;
using Marten.Exceptions;
using Marten.Schema;
using Npgsql;

namespace Marten.Events.Projections;

/// <summary>
///     Project a single document view across events that may span across
///     event streams in a user-defined grouping
/// </summary>
/// <typeparam name="TDoc"></typeparam>
/// <typeparam name="TId"></typeparam>
public abstract class MultiStreamProjection<TDoc, TId>: JasperFxMultiStreamProjectionBase<TDoc, TId, IDocumentOperations, IQuerySession>, IMartenAggregateProjection
{
    // TODO -- put the exception types in a constant somewhere
    protected MultiStreamProjection(): base([typeof(NpgsqlException), typeof(MartenCommandException)])
    {
    }

    void IMartenAggregateProjection.ConfigureAggregateMapping(DocumentMapping mapping, StoreOptions storeOptions)
    {
        mapping.UseVersionFromMatchingStream = Lifecycle == ProjectionLifecycle.Inline &&
                                               storeOptions.Events.AppendMode == EventAppendMode.Quick;
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

    // TODO -- revisit validation

    // protected override void specialAssertValid()
    // {
    //     if (_customSlicer == null && !_defaultSlicer.HasAnyRules())
    //     {
    //         throw new InvalidProjectionException(
    //             $"ViewProjection {GetType().FullNameInCode()} has no Identity() rules defined or registered lookup grouping rules and does not know how to identify event membership in the aggregated document {typeof(TDoc).FullNameInCode()}");
    //     }
    // }


    // protected override IEnumerable<string> validateDocumentIdentity(StoreOptions options, DocumentMapping mapping)
    // {
    //     if (mapping.IdType != typeof(TId))
    //     {
    //         yield return
    //             $"Id type mismatch. The projection identity type is {typeof(TId).FullNameInCode()}, but the aggregate document {typeof(TDoc).FullNameInCode()} id type is {mapping.IdType.NameInCode()}";
    //     }
    // }
}
