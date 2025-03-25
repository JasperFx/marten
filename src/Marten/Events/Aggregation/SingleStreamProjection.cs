#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.Core.Descriptions;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Schema;
using Marten.Storage;
using Npgsql;

namespace Marten.Events.Aggregation;

/// <summary>
///     Base class for aggregating events by a stream using Marten-generated pattern matching
/// </summary>
/// <typeparam name="TDoc"></typeparam>
public class SingleStreamProjection<TDoc, TId>: JasperFxSingleStreamProjectionBase<TDoc, TId, IDocumentOperations, IQuerySession>, IMartenAggregateProjection, IValidatedProjection<StoreOptions>
{
    // public override SubscriptionDescriptor Describe()
    // {
    //     return new SubscriptionDescriptor(this, SubscriptionType.SingleStreamProjection);
    // }

    public SingleStreamProjection() : base([typeof(NpgsqlException), typeof(MartenCommandException)])
    {
    }


    void IMartenAggregateProjection.ConfigureAggregateMapping(DocumentMapping mapping, StoreOptions storeOptions)
    {
        mapping.UseVersionFromMatchingStream = true;
    }

    internal bool IsIdTypeValidForStream(Type idType, StoreOptions options, out Type expectedType, out ValueTypeInfo? valueType)
    {
        valueType = default;
        expectedType = options.Events.StreamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);
        if (idType == expectedType) return true;

        valueType = options.TryFindValueType(idType);
        if (valueType == null) return false;

        return valueType.SimpleType == expectedType;
    }

    public IEnumerable<string> ValidateConfiguration(StoreOptions options)
    {
        var mapping = options.Storage.FindMapping(typeof(TDoc)).Root.As<DocumentMapping>();

        foreach (var p in validateDocumentIdentity(options, mapping)) yield return p;

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
                "SingleStreamProjection cannot support aggregates that are soft-deleted with the conventional method approach. You will need to use an explicit workflow for this projection";
        }
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
}
