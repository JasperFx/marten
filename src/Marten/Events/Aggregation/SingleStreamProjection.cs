#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.CodeGeneration;
using JasperFx.Events.Daemon;
using JasperFx.Events.Grouping;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Schema;
using Marten.Storage;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Aggregation;

/// <summary>
///     Base class for aggregating events by a stream using Marten-generated pattern matching
/// </summary>
/// <typeparam name="T"></typeparam>
public class SingleStreamProjection<T>: GeneratedAggregateProjectionBase<T>
{
    public SingleStreamProjection(): base(AggregationScope.SingleStream)
    {
    }

    public override ISubscriptionExecution BuildExecution(AsyncProjectionShard shard, DocumentStore store, IMartenDatabase database,
        ILogger logger)
    {
        var slicer = buildEventSlicer(store.Options);
        var aggregateType = _aggregateMapping.DocumentType;
        var identityType = _aggregateMapping.IdType;

        var types = new Type[]{aggregateType, identityType};

        var builder = typeof(ExecutionBuilder<,>).CloseAndBuildAs<IExecutionBuilder>(types);
        return builder.Build(slicer, shard, store, database, logger);
    }

    public override bool IsSingleStream()
    {
        return true;
    }



    protected sealed override object buildEventSlicer(StoreOptions options)
    {
        var isValidIdentity = IsIdTypeValidForStream(_aggregateMapping.IdType, options, out var idType, out var valueType);
        if (!isValidIdentity)
        {
            throw new ArgumentOutOfRangeException(
                $"{_aggregateMapping.IdType.FullNameInCode()} is not a supported stream id type for aggregate {_aggregateMapping.DocumentType.FullNameInCode()}");
        }

        if (valueType != null)
        {
            var slicerType = idType == typeof(Guid)
                ? typeof(ByStreamId<,>).MakeGenericType(_aggregateMapping.DocumentType, valueType.OuterType)
                : typeof(ByStreamKey<,>).MakeGenericType(_aggregateMapping.DocumentType, valueType.OuterType);

            return Activator.CreateInstance(slicerType, valueType)!;
        }
        else
        {
            var slicerType = idType == typeof(Guid)
                ? typeof(ByStreamId<>).MakeGenericType(_aggregateMapping.DocumentType)
                : typeof(ByStreamKey<>).MakeGenericType(_aggregateMapping.DocumentType);

            return Activator.CreateInstance(slicerType)!;
        }
    }

    public override void ConfigureAggregateMapping(IStorageMapping mapping, IEventGraph eventGraph)
    {
        mapping.UseVersionFromMatchingStream = Lifecycle == ProjectionLifecycle.Inline &&
                                               eventGraph.AppendMode == EventAppendMode.Quick;
    }

    protected sealed override Type baseTypeForAggregationRuntime()
    {
        return typeof(AggregationRuntime<,>).MakeGenericType(typeof(T), _aggregateMapping.IdType);
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

    protected sealed override IEnumerable<string> validateDocumentIdentity(StoreOptions options,
        DocumentMapping mapping)
    {
        var matches = IsIdTypeValidForStream(mapping.IdType, options, out var expectedType, out var valueTypeInfo);
        if (!matches)
        {
            yield return
                $"Id type mismatch. The stream identity type is {expectedType.NameInCode()} (or a strong typed identifier type that is convertible to {expectedType.NameInCode()}), but the aggregate document {typeof(T).FullNameInCode()} id type is {mapping.IdType.NameInCode()}";
        }

        if (valueTypeInfo != null && !mapping.IdMember.GetRawMemberType().IsNullable())
        {
            yield return
                $"At this point, Marten requires that identity members for strong typed identifiers be Nullable<T>. Change {mapping.DocumentType.FullNameInCode()}.{mapping.IdMember.Name} to a Nullable for Marten compliance";
        }
    }
}

internal interface IExecutionBuilder
{
    ISubscriptionExecution Build(object rawSlicer, AsyncProjectionShard shard, DocumentStore store, IMartenDatabase database, ILogger logger);
}

internal class ExecutionBuilder<TDoc, TId>: IExecutionBuilder
{
    public ISubscriptionExecution Build(object rawSlicer, AsyncProjectionShard shard, DocumentStore store,
        IMartenDatabase database, ILogger logger)
    {
        var slicer = (IMartenEventSlicer<TDoc, TId>)rawSlicer;
        var adapter = new MartenEventSlicerAdapter<TDoc, TId>(store, database, slicer);
        var runner = new AggregationProjectionRunner<TDoc, TId>(shard, store, database, adapter);

        return new GroupedProjectionExecution(runner, logger);
    }
}
