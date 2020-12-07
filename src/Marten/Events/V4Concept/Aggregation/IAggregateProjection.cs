using System;
using System.Collections.Generic;
using LamarCodeGeneration;

namespace Marten.Events.V4Concept.Aggregation
{
    public interface IAggregateProjection
    {
        Type AggregateType { get; }
        GeneratedType LiveAggregationType { get; set; }
        GeneratedType InlineType { get; set; }
        GeneratedType AsyncAggregationType { get; set; }
        bool WillDelete(IEnumerable<IEvent> events);
    }
}
