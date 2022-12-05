#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.CodeGeneration;
using Marten.Schema;

namespace Marten.Events.Aggregation;

/// <summary>
///     Base class for aggregating events by a stream using Marten-generated pattern matching
/// </summary>
/// <typeparam name="T"></typeparam>
public class SingleStreamAggregation<T>: GeneratedAggregateProjectionBase<T>
{
    public SingleStreamAggregation(): base(AggregationScope.SingleStream)
    {
    }

    protected sealed override object buildEventSlicer(StoreOptions documentMapping)
    {
        Type slicerType = null;
        if (_aggregateMapping.IdType == typeof(Guid))
        {
            slicerType = typeof(ByStreamId<>).MakeGenericType(_aggregateMapping.DocumentType);
        }
        else if (_aggregateMapping.IdType != typeof(string))
        {
            throw new ArgumentOutOfRangeException(
                $"{_aggregateMapping.IdType.FullNameInCode()} is not a supported stream id type for aggregate {_aggregateMapping.DocumentType.FullNameInCode()}");
        }
        else
        {
            slicerType = typeof(ByStreamKey<>).MakeGenericType(_aggregateMapping.DocumentType);
        }

        return Activator.CreateInstance(slicerType);
    }

    protected sealed override Type baseTypeForAggregationRuntime()
    {
        return typeof(AggregationRuntime<,>).MakeGenericType(typeof(T), _aggregateMapping.IdType);
    }


    protected sealed override IEnumerable<string> validateDocumentIdentity(StoreOptions options,
        DocumentMapping mapping)
    {
        switch (options.Events.StreamIdentity)
        {
            case StreamIdentity.AsGuid:
            {
                if (mapping.IdType != typeof(Guid))
                {
                    yield return
                        $"Id type mismatch. The stream identity type is System.Guid, but the aggregate document {typeof(T).FullNameInCode()} id type is {mapping.IdType.NameInCode()}";
                }

                break;
            }
            case StreamIdentity.AsString:
            {
                if (mapping.IdType != typeof(string))
                {
                    yield return
                        $"Id type mismatch. The stream identity type is string, but the aggregate document {typeof(T).FullNameInCode()} id type is {mapping.IdType.NameInCode()}";
                }

                break;
            }
        }
    }
}
