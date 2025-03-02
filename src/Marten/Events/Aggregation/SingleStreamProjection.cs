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
public class SingleStreamProjection<TDoc, TId>: JasperFxSingleStreamProjectionBase<TDoc, TId, IDocumentOperations, IQuerySession>, IMartenAggregateProjection
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
        mapping.UseVersionFromMatchingStream = Lifecycle == ProjectionLifecycle.Inline &&
                                               storeOptions.Events.AppendMode == EventAppendMode.Quick;
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

    // Redo validation
    // protected sealed override IEnumerable<string> validateDocumentIdentity(StoreOptions options,
    //     DocumentMapping mapping)
    // {
    //     var matches = IsIdTypeValidForStream(mapping.IdType, options, out var expectedType, out var valueTypeInfo);
    //     if (!matches)
    //     {
    //         yield return
    //             $"Id type mismatch. The stream identity type is {expectedType.NameInCode()} (or a strong typed identifier type that is convertible to {expectedType.NameInCode()}), but the aggregate document {typeof(TDoc).FullNameInCode()} id type is {mapping.IdType.NameInCode()}";
    //     }
    //
    //     if (valueTypeInfo != null && !mapping.IdMember.GetRawMemberType().IsNullable())
    //     {
    //         yield return
    //             $"At this point, Marten requires that identity members for strong typed identifiers be Nullable<T>. Change {mapping.DocumentType.FullNameInCode()}.{mapping.IdMember.Name} to a Nullable for Marten compliance";
    //     }
    // }
}
